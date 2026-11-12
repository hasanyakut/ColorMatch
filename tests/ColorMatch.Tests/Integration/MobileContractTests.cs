using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ColorMatch.Mobile.Models;

namespace ColorMatch.Tests.Integration;

/// <summary>
/// API ↔ mobile contract. The mobile app's own model records (compiled into this test project from <c>ColorMatch.Mobile/Models</c>) are
/// filled from REAL server responses, the way the phone's HTTP client does it (System.Text.Json web defaults). Beyond "it deserialises", each
/// check walks the JSON and fails when the server sends a property the mobile record does not have (a new field nobody mirrored) or the mobile
/// record needs a constructor value the server never sends.
/// </summary>
[Collection("api")]
public class MobileContractTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static async Task<(JsonElement Json, T Model)> Get<T>(HttpClient c, string url)
    {
        var resp = await c.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return Parse<T>(await resp.Content.ReadAsStringAsync(), url);
    }

    private static async Task<(JsonElement Json, T Model)> Post<T>(HttpClient c, string url, HttpContent? body = null)
    {
        var resp = await c.PostAsync(url, body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return Parse<T>(await resp.Content.ReadAsStringAsync(), url);
    }

    private static (JsonElement, T) Parse<T>(string json, string what)
    {
        var model = JsonSerializer.Deserialize<T>(json, Web) ?? throw new InvalidOperationException($"{what}: empty");
        var doc = JsonDocument.Parse(json).RootElement.Clone();
        var problems = new List<string>();
        Compare(doc, typeof(T), "$", problems);
        Assert.True(problems.Count == 0, $"{what} does not match the mobile model {typeof(T).Name}:\n  " + string.Join("\n  ", problems));
        return (doc, model);
    }

    private static Type Unwrap(Type t) => Nullable.GetUnderlyingType(t) ?? t;

    private static void Compare(JsonElement el, Type type, string path, List<string> problems)
    {
        type = Unwrap(type);
        switch (el.ValueKind)
        {
            case JsonValueKind.Array:
                if (type.IsGenericType && type.GetGenericArguments().Length == 1)
                    foreach (var (item, i) in el.EnumerateArray().Select((x, i) => (x, i)).Take(3))
                        Compare(item, type.GetGenericArguments()[0], $"{path}[{i}]", problems);
                break;
            case JsonValueKind.Object:
                var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
                foreach (var p in el.EnumerateObject())
                {
                    if (!props.TryGetValue(p.Name, out var prop)) { problems.Add($"{path}.{p.Name}: sent by the server, missing in {type.Name}"); continue; }
                    Compare(p.Value, prop.PropertyType, $"{path}.{p.Name}", problems);
                }
                // constructor values the server never sends (and that have no default) would silently become null / 0 on the phone
                var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
                if (ctor is not null && type.GetProperties().Length > 0 && !type.IsGenericTypeDefinition)
                    foreach (var prm in ctor.GetParameters().Where(x => !x.HasDefaultValue))
                        if (!el.EnumerateObject().Any(p => string.Equals(p.Name, prm.Name, StringComparison.OrdinalIgnoreCase)))
                            problems.Add($"{path}: {type.Name}.{prm.Name} is required by the mobile model but the server does not send it");
                break;
        }
    }

    private static byte[] Photo(string hex) => ApiFactory.SolidJpeg(hex);

    private sealed record Tiny(string Name, int Score);

    [Fact]
    public void The_contract_check_itself_catches_drift()
    {
        // the server added a field the mobile record does not have
        var extra = Assert.ThrowsAny<Exception>(() => Parse<Tiny>("{\"name\":\"a\",\"score\":1,\"newField\":true}", "t"));
        Assert.Contains("newField", extra.Message);
        // the mobile record needs a value the server does not send
        var missing = Assert.ThrowsAny<Exception>(() => Parse<Tiny>("{\"name\":\"a\"}", "t"));
        Assert.Contains("Score", missing.Message);
        // matching shapes pass
        Parse<Tiny>("{\"name\":\"a\",\"score\":1}", "t");
    }

    [Fact]
    public async Task Profile_game_screens_and_attempts_match_the_mobile_models()
    {
        var (c, _) = await factory.RegisterAsync();

        await Get<UserProfile>(c, "/api/users/me");

        var (_, hunts) = await Get<DailyHuntsState>(c, "/api/game/daily-hunts");
        Assert.Equal(3, hunts.Hunts.Count);
        var easy = hunts.Hunts.First(h => h.Difficulty == "Easy");

        // a passing attempt: reward, streak and atlas unlock fields are all present
        var (_, result) = await Post<DailyHuntResult>(c, $"/api/game/daily-hunts/{easy.Id}/attempts", ApiFactory.HuntForm(Photo(easy.Hex)));
        Assert.True(result.Passed);
        Assert.NotNull(result.AtlasUnlock);
        Assert.NotNull(result.Region);

        var (_, week) = await Get<WeekData>(c, "/api/game/daily-hunts/week");
        Assert.Equal(7, week.Days.Count);
        Assert.Equal(1, week.ActiveDays);

        var (_, atlas) = await Get<AtlasData>(c, "/api/game/atlas");
        Assert.Equal(100, atlas.Total);
        Assert.True(atlas.Unlocked >= 1);

        var (_, board) = await Get<BingoBoard>(c, "/api/game/bingo");
        var cell = board.Cells[4];
        var (_, bingo) = await Post<BingoResult>(c, $"/api/game/bingo/cells/{cell.Index}/attempts", ApiFactory.HuntForm(Photo(cell.Hex)));
        Assert.True(bingo.CellCompleted);
        Assert.NotNull(bingo.Region);
    }

    [Fact]
    public async Task Friend_duel_screens_and_attempts_match_the_mobile_models()
    {
        var (a, ua) = await factory.RegisterAsync();
        var (b, ub) = await factory.RegisterAsync();
        await a.PostAsJsonAsync("/api/friends/requests", new { userId = ub.User.Id });
        await b.PostAsync($"/api/friends/requests/{ua.User.Id}/accept", null);

        await Get<List<Friend>>(a, "/api/friends");
        await Get<FriendRequests>(a, "/api/friends/requests");
        await Get<List<UserSearchResult>>(a, $"/api/friends/search?q={Uri.EscapeDataString(ub.User.UserName[..4])}");
        await Get<RelationshipResult>(a, $"/api/friends/status/{ub.User.Id}");

        var created = await a.PostAsJsonAsync("/api/game/duels", new CreateDuelBody(ub.User.Id));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Parse<Duel>(await created.Content.ReadAsStringAsync(), "create duel");

        var (_, list) = await Get<DuelList>(b, "/api/game/duels");
        var invite = Assert.Single(list.Duels);
        Assert.True(invite.NeedsMyResponse);

        var (_, accepted) = await Post<Duel>(b, $"/api/game/duels/{invite.Id}/accept");
        Assert.True(accepted.CanPlay);

        var (_, attempt) = await Post<DuelAttemptResult>(b, $"/api/game/duels/{invite.Id}/attempts", ApiFactory.HuntForm(Photo(accepted.TargetHex!)));
        Assert.NotNull(attempt.Region);
        Assert.Equal(2, attempt.AttemptsLeft);
        await Post<Duel>(b, $"/api/game/duels/{invite.Id}/finish");
    }

    [Fact]
    public async Task Social_feed_and_posts_match_the_mobile_models()
    {
        var (c, _) = await factory.RegisterAsync();
        var up = await c.PostAsync("/api/media", ApiFactory.FileForm(Photo("#E86C52")));
        Assert.True(up.IsSuccessStatusCode, await up.Content.ReadAsStringAsync());
        var media = (await up.Content.ReadFromJsonAsync<MediaUpload>(Web))!;
        var post = await c.PostAsJsonAsync("/api/posts", new CreatePostBody(PostType.Photo, "test", "#E86C52", [media.Key]), Web);
        Assert.True(post.IsSuccessStatusCode, await post.Content.ReadAsStringAsync());
        var (_, created) = Parse<Post>(await post.Content.ReadAsStringAsync(), "create post");
        try
        {
            var (_, feed) = await Get<Paged<Post>>(c, "/api/posts?scope=all&pageSize=5");
            Assert.NotEmpty(feed.Items);
            await Get<List<ColorFacet>>(c, "/api/posts/colors");
        }
        finally
        {
            // the global feed is shared by other tests that count its posts: leave it as it was
            await c.DeleteAsync($"/api/posts/{created.Id}");
        }
    }
}
