using System.Net;
using System.Net.Http.Json;
using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.Domain.Enums;
using ColorMatch.Domain.Rules;

namespace ColorMatch.Tests.Integration;

[Collection("api")]
public class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Register_login_and_read_profile()
    {
        var (client, auth) = await factory.RegisterAsync("alice_a");
        Assert.Equal("alice_a", auth.User.UserName);
        Assert.Equal(0, auth.User.TotalXp);
        Assert.Equal(1, auth.User.Level);

        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("alice_a@example.com", "Passw0rd!"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await client.GetFromJsonAsync<UserProfileDto>("/api/users/me");
        Assert.Equal(auth.User.Id, me!.Id);
    }

    [Fact]
    public async Task Login_with_user_name_works_and_wrong_password_is_401()
    {
        await factory.RegisterAsync("bob_b");
        var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/api/auth/login", new LoginRequest("bob_b", "Passw0rd!"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/auth/login", new LoginRequest("bob_b", "wrong-password1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody", "Passw0rd!"))).StatusCode);
    }

    [Fact]
    public async Task Duplicate_registration_and_weak_password_return_400()
    {
        await factory.RegisterAsync("carol_c");
        var http = factory.CreateClient();
        var dup = await http.PostAsJsonAsync("/api/auth/register", new RegisterRequest("other@example.com", "carol_c", "Passw0rd!"));
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        var weak = await http.PostAsJsonAsync("/api/auth/register", new RegisterRequest("weak@example.com", "weakuser", "passwordonly"));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        var badName = await http.PostAsJsonAsync("/api/auth/register", new RegisterRequest("x@example.com", "a b", "Passw0rd!"));
        Assert.Equal(HttpStatusCode.BadRequest, badName.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoints_require_a_token()
    {
        var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/challenges/today")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/posts")).StatusCode);
    }
}

[Collection("api")]
public class ColorHuntTests(ApiFactory factory)
{
    [Fact]
    public async Task Daily_challenge_is_same_for_every_user_and_valid_hex()
    {
        var (c1, _) = await factory.RegisterAsync();
        var (c2, _) = await factory.RegisterAsync();
        var a = await c1.GetFromJsonAsync<DailyChallengeDto>("/api/challenges/today");
        var b = await c2.GetFromJsonAsync<DailyChallengeDto>("/api/challenges/today");
        Assert.Equal(a!.Id, b!.Id);
        Assert.Equal(a.Hex, b.Hex);
        Assert.Matches("^#[0-9A-F]{6}$", a.Hex);
        Assert.True(a.ResetsAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Exact_colour_scores_near_1000_and_xp_is_only_paid_once()
    {
        var (client, auth) = await factory.RegisterAsync();
        var today = (await client.GetFromJsonAsync<DailyChallengeDto>("/api/challenges/today"))!;

        var first = await Submit(client, ApiFactory.SolidJpeg(today.Hex));
        Assert.InRange(first.Score, 960, 1000);
        Assert.Equal(XpRules.XpForScore(first.Score), first.XpAwarded);
        Assert.True(first.IsNewDailyBest);
        Assert.Equal(first.XpAwarded, first.TotalXp);
        Assert.Equal(today.Hex, first.TargetHex);

        // replaying the same quality must not give XP again
        var second = await Submit(client, ApiFactory.SolidJpeg(today.Hex));
        Assert.Equal(0, second.XpAwarded);
        Assert.Equal(first.TotalXp, second.TotalXp);

        // photo is retrievable through /media
        var photo = await client.GetAsync(first.PhotoUrl);
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);

        var me = await client.GetFromJsonAsync<UserProfileDto>("/api/users/me");
        Assert.Equal(first.TotalXp, me!.TotalXp);
        var after = (await client.GetFromJsonAsync<DailyChallengeDto>("/api/challenges/today"))!;
        Assert.Equal(2, after.MyAttemptsToday);
        Assert.Equal(first.TotalXp, after.MyXpToday);
        Assert.Equal(Math.Max(first.Score, second.Score), after.MyBestScore);
        Assert.Equal(auth.User.Id, me.Id);
    }

    [Fact]
    public async Task Improving_the_best_score_pays_only_the_difference()
    {
        var (client, _) = await factory.RegisterAsync();
        var today = (await client.GetFromJsonAsync<DailyChallengeDto>("/api/challenges/today"))!;

        // a clearly worse colour: invert the channels
        var rgb = ColorMatch.ColorEngine.ColorConversion.Rgb.FromHex(today.Hex);
        var bad = new ColorMatch.ColorEngine.ColorConversion.Rgb((byte)(255 - rgb.R), (byte)(255 - rgb.G), (byte)(255 - rgb.B)).ToHex();
        var poor = await Submit(client, ApiFactory.SolidJpeg(bad));
        Assert.True(poor.Score < 600);
        Assert.Equal(XpRules.XpForScore(poor.Score), poor.XpAwarded);

        var good = await Submit(client, ApiFactory.SolidJpeg(today.Hex));
        Assert.Equal(XpRules.XpForScore(good.Score) - poor.XpAwarded, good.XpAwarded);
        Assert.Equal(XpRules.XpForScore(good.Score), good.TotalXp);
        Assert.True(good.IsNewDailyBest);
    }

    [Fact]
    public async Task The_server_finds_the_best_region_itself_whatever_region_the_client_sends()
    {
        var (client, _) = await factory.RegisterAsync();
        var today = (await client.GetFromJsonAsync<DailyChallengeDto>("/api/challenges/today"))!;

        // left half = target colour, right half = bright blue; the client points at the blue half on purpose
        using var bmp = new SkiaSharp.SKBitmap(200, 200);
        var t = ColorMatch.ColorEngine.ColorConversion.Rgb.FromHex(today.Hex);
        for (var y = 0; y < 200; y++)
            for (var x = 0; x < 200; x++)
                bmp.SetPixel(x, y, x < 100 ? new SkiaSharp.SKColor(t.R, t.G, t.B) : new SkiaSharp.SKColor(0, 0, 255));
        using var img = SkiaSharp.SKImage.FromBitmap(bmp);
        var jpeg = img.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 95).ToArray();

        var onTarget = await client.PostAsync("/api/color-hunts", ApiFactory.HuntForm(jpeg, 0.2, 0.5, 0.05));
        var onBlue = await client.PostAsync("/api/color-hunts", ApiFactory.HuntForm(jpeg, 0.8, 0.5, 0.05));
        var a = (await onTarget.Content.ReadFromJsonAsync<HuntAttemptResultDto>())!;
        var b = (await onBlue.Content.ReadFromJsonAsync<HuntAttemptResultDto>())!;
        Assert.True(a.Score > 900, $"score {a.Score}");
        Assert.True(b.Score > 900, $"the client-sent region must not matter, score {b.Score}");
        Assert.NotNull(b.Region);
        Assert.InRange(b.Region!.X, 0.0, 0.55);   // measured on the target-coloured half
    }

    [Fact]
    public async Task A_small_patch_of_the_target_colour_in_a_busy_photo_is_found_and_reported()
    {
        var (client, _) = await factory.RegisterAsync();
        var today = (await client.GetFromJsonAsync<DailyChallengeDto>("/api/challenges/today"))!;
        var t = ColorMatch.ColorEngine.ColorConversion.Rgb.FromHex(today.Hex);

        // neutral grey photo with a 40×40 patch of the target low on the right (centre ≈ 0.75, 0.70)
        using var bmp = new SkiaSharp.SKBitmap(200, 200);
        for (var y = 0; y < 200; y++)
            for (var x = 0; x < 200; x++)
                bmp.SetPixel(x, y, x is >= 130 and < 170 && y is >= 120 and < 160 ? new SkiaSharp.SKColor(t.R, t.G, t.B) : new SkiaSharp.SKColor(128, 128, 128));
        using var img = SkiaSharp.SKImage.FromBitmap(bmp);
        var jpeg = img.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 95).ToArray();

        var resp = await client.PostAsync("/api/color-hunts", ApiFactory.HuntForm(jpeg, 0.1, 0.1, 0.02));
        var r = (await resp.Content.ReadFromJsonAsync<HuntAttemptResultDto>())!;
        Assert.True(r.Score > 900, $"score {r.Score}");
        Assert.InRange(r.Region!.X, 0.65, 0.85);
        Assert.InRange(r.Region.Y, 0.60, 0.80);
    }

    [Fact]
    public async Task Invalid_inputs_are_rejected_with_400()
    {
        var (client, _) = await factory.RegisterAsync();
        var notImage = await client.PostAsync("/api/color-hunts", ApiFactory.HuntForm("hello world, not an image"u8.ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, notImage.StatusCode);

        // a nonsense region is simply ignored now (the server picks the region), it is not an error
        var ignored = await client.PostAsync("/api/color-hunts", ApiFactory.HuntForm(ApiFactory.SolidJpeg("#FF0000"), 1.5, -3, 0.9));
        Assert.Equal(HttpStatusCode.OK, ignored.StatusCode);
    }

    private static async Task<HuntAttemptResultDto> Submit(HttpClient client, byte[] jpeg)
    {
        var resp = await client.PostAsync("/api/color-hunts", ApiFactory.HuntForm(jpeg));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<HuntAttemptResultDto>())!;
    }
}

[Collection("api")]
public class SocialTests(ApiFactory factory)
{
    [Fact]
    public async Task Upload_publish_like_comment_and_paginate()
    {
        var (alice, aliceAuth) = await factory.RegisterAsync();
        var (bob, _) = await factory.RegisterAsync();

        var keys = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var up = await alice.PostAsync("/api/media", ApiFactory.FileForm(ApiFactory.SolidJpeg("#3366CC", 64 + i)));
            Assert.Equal(HttpStatusCode.Created, up.StatusCode);
            keys.Add((await up.Content.ReadFromJsonAsync<MediaUploadResponse>())!.Key);
        }

        var ids = new List<Guid>();
        foreach (var key in keys)
        {
            var created = await alice.PostAsJsonAsync("/api/posts", new CreatePostRequest(PostType.Photo, "hello", "#3366cc", [key]));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            ids.Add((await created.Content.ReadFromJsonAsync<PostDto>())!.Id);
            await Task.Delay(15);
        }

        // pagination: newest first, 2 + 1
        var page1 = (await bob.GetFromJsonAsync<PagedResult<PostDto>>("/api/posts?pageSize=2"))!;
        Assert.Equal(2, page1.Items.Count);
        Assert.NotNull(page1.NextCursor);
        Assert.Equal(ids[2], page1.Items[0].Id);
        var page2 = (await bob.GetFromJsonAsync<PagedResult<PostDto>>($"/api/posts?pageSize=2&cursor={Uri.EscapeDataString(page1.NextCursor!)}"))!;
        Assert.Single(page2.Items);
        Assert.Null(page2.NextCursor);
        Assert.Equal(ids[0], page2.Items[0].Id);
        Assert.Equal("#3366CC", page2.Items[0].ColorHex);
        Assert.Equal(aliceAuth.User.UserName, page2.Items[0].Author.UserName);

        // likes are idempotent, unlike works
        var postId = ids[0];
        var l1 = await (await bob.PutAsync($"/api/posts/{postId}/like", null)).Content.ReadFromJsonAsync<LikeStateDto>();
        var l2 = await (await bob.PutAsync($"/api/posts/{postId}/like", null)).Content.ReadFromJsonAsync<LikeStateDto>();
        Assert.True(l1!.Liked); Assert.Equal(1, l2!.LikeCount);
        var viewed = await bob.GetFromJsonAsync<PostDto>($"/api/posts/{postId}");
        Assert.True(viewed!.LikedByMe);
        var l3 = await (await bob.DeleteAsync($"/api/posts/{postId}/like")).Content.ReadFromJsonAsync<LikeStateDto>();
        Assert.False(l3!.Liked); Assert.Equal(0, l3.LikeCount);

        // comments
        var c = await bob.PostAsJsonAsync($"/api/posts/{postId}/comments", new CreateCommentRequest("nice colour!"));
        Assert.Equal(HttpStatusCode.Created, c.StatusCode);
        var comments = await alice.GetFromJsonAsync<PagedResult<CommentDto>>($"/api/posts/{postId}/comments");
        Assert.Single(comments!.Items);
        Assert.Equal(1, (await alice.GetFromJsonAsync<PostDto>($"/api/posts/{postId}"))!.CommentCount);
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.PostAsJsonAsync($"/api/posts/{postId}/comments", new CreateCommentRequest(""))).StatusCode);

        // profile
        var profile = await bob.GetFromJsonAsync<UserProfileDto>($"/api/users/{aliceAuth.User.Id}");
        Assert.Equal(3, profile!.PostCount);
        var userPosts = await bob.GetFromJsonAsync<PagedResult<PostDto>>($"/api/users/{aliceAuth.User.Id}/posts");
        Assert.Equal(3, userPosts!.Items.Count);

        // only the author may delete
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/posts/{postId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/posts/{postId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/posts/{postId}")).StatusCode);
    }

    [Fact]
    public async Task Cannot_publish_someone_elses_media_or_non_images()
    {
        var (alice, _) = await factory.RegisterAsync();
        var (mallory, _) = await factory.RegisterAsync();

        var up = await alice.PostAsync("/api/media", ApiFactory.FileForm(ApiFactory.SolidJpeg("#FF8800")));
        var key = (await up.Content.ReadFromJsonAsync<MediaUploadResponse>())!.Key;

        var steal = await mallory.PostAsJsonAsync("/api/posts", new CreatePostRequest(PostType.Photo, null, null, [key]));
        Assert.Equal(HttpStatusCode.BadRequest, steal.StatusCode);

        var traversal = await mallory.PostAsJsonAsync("/api/posts", new CreatePostRequest(PostType.Photo, null, null, ["../../etc/passwd"]));
        Assert.Equal(HttpStatusCode.BadRequest, traversal.StatusCode);

        var text = await mallory.PostAsync("/api/media", ApiFactory.FileForm("just text"u8.ToArray(), "text/plain", "a.txt"));
        Assert.Equal(HttpStatusCode.BadRequest, text.StatusCode);
    }

    [Fact]
    public async Task Avatar_upload_updates_profile()
    {
        var (client, _) = await factory.RegisterAsync();
        var resp = await client.PostAsync("/api/users/me/avatar", ApiFactory.FileForm(ApiFactory.SolidJpeg("#22AA66", 600)));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var profile = await resp.Content.ReadFromJsonAsync<UserProfileDto>();
        Assert.NotNull(profile!.AvatarUrl);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(profile.AvatarUrl)).StatusCode);
    }
}

[Collection("api")]
public class HomeExtrasTests(ApiFactory factory)
{
    [Fact]
    public async Task Leaderboard_ranks_players_by_best_score_today()
    {
        var (champ, champAuth) = await factory.RegisterAsync();
        var (rookie, rookieAuth) = await factory.RegisterAsync();
        var (idle, _) = await factory.RegisterAsync();
        var today = (await champ.GetFromJsonAsync<DailyChallengeDto>("/api/challenges/today"))!;

        var rgb = ColorMatch.ColorEngine.ColorConversion.Rgb.FromHex(today.Hex);
        var off = new ColorMatch.ColorEngine.ColorConversion.Rgb((byte)Math.Min(255, rgb.R + 40), rgb.G, (byte)Math.Max(0, rgb.B - 40)).ToHex();

        await champ.PostAsync("/api/color-hunts", ApiFactory.HuntForm(ApiFactory.SolidJpeg(today.Hex)));
        await rookie.PostAsync("/api/color-hunts", ApiFactory.HuntForm(ApiFactory.SolidJpeg(off)));

        var board = (await rookie.GetFromJsonAsync<LeaderboardDto>("/api/challenges/today/leaderboard"))!;
        var champRow = board.Entries.Single(e => e.UserId == champAuth.User.Id);
        var rookieRow = board.Entries.Single(e => e.UserId == rookieAuth.User.Id);
        Assert.True(champRow.Rank < rookieRow.Rank);
        Assert.True(champRow.BestScore > rookieRow.BestScore);
        Assert.True(rookieRow.IsMe);
        Assert.False(champRow.IsMe);
        Assert.Equal(rookieRow.Rank, board.MyRank);
        Assert.True(board.TotalPlayers >= 2);

        // a player who has not played has no rank
        var idleBoard = (await idle.GetFromJsonAsync<LeaderboardDto>("/api/challenges/today/leaderboard"))!;
        Assert.Null(idleBoard.MyRank);
    }

    [Fact]
    public async Task Streak_starts_at_one_after_the_first_hunt()
    {
        var (client, _) = await factory.RegisterAsync();
        var before = (await client.GetFromJsonAsync<UserProfileDto>("/api/users/me"))!;
        Assert.Equal(0, before.StreakDays);

        var today = (await client.GetFromJsonAsync<DailyChallengeDto>("/api/challenges/today"))!;
        await client.PostAsync("/api/color-hunts", ApiFactory.HuntForm(ApiFactory.SolidJpeg(today.Hex)));

        var after = (await client.GetFromJsonAsync<UserProfileDto>("/api/users/me"))!;
        Assert.Equal(1, after.StreakDays);
    }

    [Fact]
    public async Task Recent_returns_previous_days_newest_first()
    {
        var (client, _) = await factory.RegisterAsync();
        var recent = (await client.GetFromJsonAsync<List<PastChallengeDto>>("/api/challenges/recent?days=5"))!;
        Assert.Equal(5, recent.Count);
        Assert.True(recent.Zip(recent.Skip(1), (a, b) => a.Date > b.Date).All(x => x));
        Assert.All(recent, r => Assert.Matches("^#[0-9A-F]{6}$", r.Hex));
        Assert.All(recent, r => Assert.Equal(0, r.MyBestScore));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/challenges/recent")).StatusCode);
    }
}
