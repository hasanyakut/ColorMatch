using System.Net;
using System.Net.Http.Json;
using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.ColorEngine.ColorAnalysis;
using ColorMatch.Domain.Entities;
using ColorMatch.Domain.Enums;
using ColorMatch.Tests.Integration;

namespace ColorMatch.Tests;

public class ColorFamiliesTests
{
    [Theory]
    [InlineData("#D9392B", "Kırmızı")]
    [InlineData("#F07F1A", "Turuncu")]
    [InlineData("#F4DE4A", "Sarı")]
    [InlineData("#5CAD3F", "Yeşil")]
    [InlineData("#2BB5B0", "Turkuaz")]
    [InlineData("#2A56C6", "Mavi")]
    [InlineData("#6B3FA0", "Mor")]
    [InlineData("#F2A0B8", "Pembe")]
    [InlineData("#7A4E2D", "Kahverengi")]
    [InlineData("#FFFFFF", "Beyaz")]
    [InlineData("#8A8D91", "Gri")]
    [InlineData("#16181C", "Siyah")]
    public void Classifies_representative_colours(string hex, string family) => Assert.Equal(family, ColorFamilies.Classify(hex));

    [Fact]
    public void Every_family_representative_maps_to_itself()
    {
        foreach (var f in ColorFamilies.All)
            Assert.Equal(f.Name, ColorFamilies.Classify(f.Hex));
    }

    [Fact]
    public void Invalid_hex_has_no_family() => Assert.Null(ColorFamilies.Classify("nope"));

    [Fact]
    public void Friendship_pair_key_is_order_independent()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        Assert.Equal(Friendship.MakePairKey(a, b), Friendship.MakePairKey(b, a));
    }
}

[Collection("api")]
public class FriendsApiTests(ApiFactory factory)
{
    private static async Task<RelationshipDto> Rel(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<RelationshipDto>())!;
    }

    [Fact]
    public async Task Request_accept_list_and_unfriend()
    {
        var (alice, a) = await factory.RegisterAsync();
        var (bob, b) = await factory.RegisterAsync();

        Assert.Equal("request_sent", (await Rel(await alice.PostAsJsonAsync("/api/friends/requests", new SendFriendRequestBody(b.User.Id)))).Relationship);
        // idempotent
        Assert.Equal("request_sent", (await Rel(await alice.PostAsJsonAsync("/api/friends/requests", new SendFriendRequestBody(b.User.Id)))).Relationship);

        Assert.Equal("request_received", (await bob.GetFromJsonAsync<RelationshipDto>($"/api/friends/status/{a.User.Id}"))!.Relationship);
        var bobReqs = (await bob.GetFromJsonAsync<FriendRequestsDto>("/api/friends/requests"))!;
        Assert.Contains(bobReqs.Incoming, f => f.UserId == a.User.Id);
        var aliceReqs = (await alice.GetFromJsonAsync<FriendRequestsDto>("/api/friends/requests"))!;
        Assert.Contains(aliceReqs.Outgoing, f => f.UserId == b.User.Id);

        // only the addressee can accept
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsync($"/api/friends/requests/{b.User.Id}/accept", null)).StatusCode);
        Assert.Equal("friends", (await Rel(await bob.PostAsync($"/api/friends/requests/{a.User.Id}/accept", null))).Relationship);

        Assert.Contains((await alice.GetFromJsonAsync<List<FriendDto>>("/api/friends"))!, f => f.UserId == b.User.Id);
        Assert.Contains((await bob.GetFromJsonAsync<List<FriendDto>>("/api/friends"))!, f => f.UserId == a.User.Id);

        Assert.Equal("none", (await Rel(await alice.DeleteAsync($"/api/friends/{b.User.Id}"))).Relationship);
        Assert.Empty((await bob.GetFromJsonAsync<List<FriendDto>>("/api/friends"))!);
    }

    [Fact]
    public async Task Crossed_requests_become_a_friendship_and_self_requests_are_rejected()
    {
        var (alice, a) = await factory.RegisterAsync();
        var (bob, b) = await factory.RegisterAsync();

        await alice.PostAsJsonAsync("/api/friends/requests", new SendFriendRequestBody(b.User.Id));
        Assert.Equal("friends", (await Rel(await bob.PostAsJsonAsync("/api/friends/requests", new SendFriendRequestBody(a.User.Id)))).Relationship);

        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/friends/requests", new SendFriendRequestBody(a.User.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsJsonAsync("/api/friends/requests", new SendFriendRequestBody(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task Decline_removes_the_request()
    {
        var (alice, a) = await factory.RegisterAsync();
        var (bob, b) = await factory.RegisterAsync();
        await alice.PostAsJsonAsync("/api/friends/requests", new SendFriendRequestBody(b.User.Id));
        Assert.Equal("none", (await Rel(await bob.DeleteAsync($"/api/friends/{a.User.Id}"))).Relationship);
        Assert.Empty((await bob.GetFromJsonAsync<FriendRequestsDto>("/api/friends/requests"))!.Incoming);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/friends/requests/{a.User.Id}/accept", null)).StatusCode);
    }

    [Fact]
    public async Task Search_finds_by_prefix_with_relationship_and_rejects_short_queries()
    {
        var tag = "zq" + Random.Shared.Next(100000, 999999);
        var (alice, a) = await factory.RegisterAsync();
        var (carol, c) = await factory.RegisterAsync(tag + "_carol");
        var (dave, _) = await factory.RegisterAsync(tag + "_dave");

        var results = (await alice.GetFromJsonAsync<List<UserSearchResultDto>>($"/api/friends/search?q={tag}"))!;
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("none", r.Relationship));

        await alice.PostAsJsonAsync("/api/friends/requests", new SendFriendRequestBody(c.User.Id));
        results = (await alice.GetFromJsonAsync<List<UserSearchResultDto>>($"/api/friends/search?q={tag}_c"))!;
        Assert.Equal("request_sent", Assert.Single(results).Relationship);

        // wildcards typed by the user are literal, and the caller never finds themself
        Assert.Empty((await alice.GetFromJsonAsync<List<UserSearchResultDto>>("/api/friends/search?q=%25%25"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync("/api/friends/search?q=a")).StatusCode);
        Assert.DoesNotContain((await alice.GetFromJsonAsync<List<UserSearchResultDto>>($"/api/friends/search?q={a.User.UserName}"))!, r => r.UserId == a.User.Id);
    }

    [Fact]
    public async Task Friends_feed_shows_friends_and_self_while_discovery_shows_everyone_filtered_by_colour()
    {
        var (alice, a) = await factory.RegisterAsync();
        var (bob, b) = await factory.RegisterAsync();
        var (eve, e) = await factory.RegisterAsync();

        async Task<Guid> Post(HttpClient c, string hex)
        {
            var up = await c.PostAsync("/api/media", ApiFactory.FileForm(ApiFactory.SolidJpeg(hex)));
            var key = (await up.Content.ReadFromJsonAsync<MediaUploadResponse>())!.Key;
            var r = await c.PostAsJsonAsync("/api/posts", new CreatePostRequest(PostType.Photo, "x", hex, [key]));
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            return (await r.Content.ReadFromJsonAsync<PostDto>())!.Id;
        }

        var bobBlue = await Post(bob, "#2A56C6");
        var eveBlue = await Post(eve, "#1F3FA8");
        var eveRed = await Post(eve, "#D9392B");
        var aliceGreen = await Post(alice, "#4CAF50");

        // before friending: friends feed has only my own post
        var onlyMine = (await alice.GetFromJsonAsync<PagedResult<PostDto>>("/api/posts?scope=friends&pageSize=50"))!;
        Assert.Equal([aliceGreen], onlyMine.Items.Select(p => p.Id).ToArray());

        await alice.PostAsJsonAsync("/api/friends/requests", new SendFriendRequestBody(b.User.Id));
        // a pending request must not leak the other person's posts
        Assert.DoesNotContain((await alice.GetFromJsonAsync<PagedResult<PostDto>>("/api/posts?scope=friends&pageSize=50"))!.Items, p => p.Id == bobBlue);
        await bob.PostAsync($"/api/friends/requests/{a.User.Id}/accept", null);

        var friendsFeed = (await alice.GetFromJsonAsync<PagedResult<PostDto>>("/api/posts?scope=friends&pageSize=50"))!.Items.Select(p => p.Id).ToList();
        Assert.Contains(bobBlue, friendsFeed);
        Assert.Contains(aliceGreen, friendsFeed);
        Assert.DoesNotContain(eveBlue, friendsFeed);
        Assert.DoesNotContain(eveRed, friendsFeed);

        // discovery: everyone, no friendship needed; colour filter
        var blue = (await alice.GetFromJsonAsync<PagedResult<PostDto>>("/api/posts?scope=all&color=Mavi&pageSize=50"))!.Items.Select(p => p.Id).ToList();
        Assert.Contains(bobBlue, blue);
        Assert.Contains(eveBlue, blue);
        Assert.DoesNotContain(eveRed, blue);
        Assert.DoesNotContain(aliceGreen, blue);

        // friends + colour combine
        var friendBlue = (await alice.GetFromJsonAsync<PagedResult<PostDto>>("/api/posts?scope=friends&color=Mavi&pageSize=50"))!.Items.Select(p => p.Id).ToList();
        Assert.Contains(bobBlue, friendBlue);
        Assert.DoesNotContain(eveBlue, friendBlue);

        var facets = (await alice.GetFromJsonAsync<List<ColorFacetDto>>("/api/posts/colors"))!;
        Assert.True(facets.Single(f => f.Family == "Mavi").Count >= 2);
        Assert.Contains(facets, f => f.Family == "Kırmızı");

        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync("/api/posts?color=Neon")).StatusCode);
        // unfriending removes their posts from the friends feed again
        await alice.DeleteAsync($"/api/friends/{b.User.Id}");
        Assert.DoesNotContain((await alice.GetFromJsonAsync<PagedResult<PostDto>>("/api/posts?scope=friends&pageSize=50"))!.Items, p => p.Id == bobBlue);
    }

    [Fact]
    public async Task Friends_endpoints_require_authentication()
    {
        var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/friends")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/posts/colors")).StatusCode);
    }
}
