using System.Net;
using System.Net.Http.Json;
using ColorMatch.Application.DTOs;
using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.Domain.Entities;
using ColorMatch.Domain.Enums;
using ColorMatch.Domain.Rules;
using ColorMatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ColorMatch.Tests.Integration;

[Collection("api")]
public class AtlasApiTests(ApiFactory factory)
{
    private static Task<AtlasDto> GetAtlas(HttpClient c) => c.GetFromJsonAsync<AtlasDto>("/api/game/atlas")!;
    private static Task<DailyHuntsDto> Today(HttpClient c) => c.GetFromJsonAsync<DailyHuntsDto>("/api/game/daily-hunts")!;
    private static DailyHuntDto Of(DailyHuntsDto d, string difficulty) => d.Hunts.Single(h => h.Difficulty == difficulty);

    private static async Task<DailyHuntAttemptResultDto> Attempt(HttpClient c, DailyHuntDto hunt, byte[] jpeg)
    {
        var resp = await c.PostAsync($"/api/game/daily-hunts/{hunt.Id}/attempts", ApiFactory.HuntForm(jpeg, 0.5, 0.5, 0.1));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<DailyHuntAttemptResultDto>())!;
    }

    private static byte[] Wrong(DailyHuntDto hunt)
    {
        var c = Rgb.FromHex(hunt.Hex);
        return ApiFactory.SolidJpeg(new Rgb((byte)(255 - c.R), (byte)(255 - c.G), (byte)(255 - c.B)).ToHex());
    }

    [Fact]
    public async Task The_atlas_requires_authentication()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/game/atlas")).StatusCode);

    [Fact]
    public async Task A_new_player_sees_five_locked_collections_and_only_todays_targets_are_revealed()
    {
        var (c, _) = await factory.RegisterAsync();
        var hunts = await Today(c);
        var atlas = await GetAtlas(c);

        Assert.Equal(5, atlas.Collections.Count);
        Assert.Equal(100, atlas.Total);
        Assert.Equal(0, atlas.Unlocked);
        Assert.All(atlas.Collections, col => { Assert.Equal(20, col.Total); Assert.Equal(0, col.Unlocked); Assert.False(col.Completed); Assert.Equal(500, col.CompletionXp); });

        var colours = atlas.Collections.SelectMany(x => x.Colors).ToList();
        Assert.Equal(100, colours.Count);
        Assert.All(colours, x => Assert.False(x.Unlocked));
        // locked colours hide name and hex, except the three the client already knows from today's hunts
        var revealed = colours.Where(x => x.Hex is not null).ToList();
        Assert.Equal(3, revealed.Count);
        Assert.All(revealed, x => Assert.True(x.IsTodaysHunt));
        Assert.Equal(hunts.Hunts.Select(h => h.Hex).OrderBy(h => h), revealed.Select(x => x.Hex!).OrderBy(h => h));
        Assert.All(colours.Where(x => !x.IsTodaysHunt), x => { Assert.Null(x.Name); Assert.Null(x.Hex); });
    }

    [Fact]
    public async Task A_failed_attempt_unlocks_nothing_and_a_valid_one_unlocks_the_hunts_colour_once()
    {
        var (c, _) = await factory.RegisterAsync();
        var easy = Of(await Today(c), "Easy");

        var bad = await Attempt(c, easy, Wrong(easy));
        Assert.False(bad.Passed);
        Assert.Null(bad.AtlasUnlock);
        Assert.Equal(0, (await GetAtlas(c)).Unlocked);

        var good = await Attempt(c, easy, ApiFactory.SolidJpeg(easy.Hex));
        Assert.True(good.Passed);
        Assert.NotNull(good.AtlasUnlock);
        var expected = AtlasCatalog.FindByHex(easy.Hex)!;
        Assert.Equal(expected.Key, good.AtlasUnlock!.ColorKey);
        Assert.Equal(expected.Name, good.AtlasUnlock.Name);
        Assert.Equal(1, good.AtlasUnlock.CollectionUnlocked);
        Assert.False(good.AtlasUnlock.CollectionCompleted);
        Assert.Equal(0, good.AtlasUnlock.CompletionXpAwarded);
        Assert.Equal(100, good.TotalXp);   // unlocking a colour itself pays no XP

        var atlas = await GetAtlas(c);
        Assert.Equal(1, atlas.Unlocked);
        var unlocked = atlas.Collections.SelectMany(x => x.Colors).Single(x => x.Unlocked);
        Assert.Equal(expected.Key, unlocked.Key);
        Assert.Equal(expected.Hex, unlocked.Hex);
        Assert.True(unlocked.BestScore >= 700);

        // replaying the same hunt does not unlock or pay again
        var replay = await Attempt(c, easy, ApiFactory.SolidJpeg(easy.Hex));
        Assert.True(replay.Passed);
        Assert.Null(replay.AtlasUnlock);
        Assert.Equal(1, (await GetAtlas(c)).Unlocked);
    }

    [Fact]
    public async Task Atlas_progress_is_per_player()
    {
        var (a, _) = await factory.RegisterAsync();
        var (b, _) = await factory.RegisterAsync();
        var easy = Of(await Today(a), "Easy");
        await Attempt(a, easy, ApiFactory.SolidJpeg(easy.Hex));

        Assert.Equal(1, (await GetAtlas(a)).Unlocked);
        Assert.Equal(0, (await GetAtlas(b)).Unlocked);
    }

    [Fact]
    public async Task Parallel_valid_attempts_unlock_the_colour_once()
    {
        var (c, auth) = await factory.RegisterAsync();
        var hard = Of(await Today(c), "Hard");

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
            await c.PostAsync($"/api/game/daily-hunts/{hard.Id}/attempts", ApiFactory.HuntForm(ApiFactory.SolidJpeg(hard.Hex), 0.5, 0.5, 0.1))));
        Assert.All(results, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict));

        Assert.Equal(1, (await GetAtlas(c)).Unlocked);
        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.UserAtlasColors.CountAsync(u => u.UserId == auth.User.Id));
    }

    [Fact]
    public async Task Finishing_a_collection_pays_the_completion_xp_exactly_once()
    {
        var (c, auth) = await factory.RegisterAsync();
        var easy = Of(await Today(c), "Easy");
        var target = AtlasCatalog.FindByHex(easy.Hex)!;
        var collection = AtlasCatalog.Collection(target.CollectionKey)!;

        // nineteen colours were unlocked earlier (seeded); today's easy hunt unlocks the twentieth
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var col in collection.Colors.Where(x => x.Key != target.Key))
                db.UserAtlasColors.Add(new UserAtlasColor { Id = Guid.NewGuid(), UserId = auth.User.Id, ColorKey = col.Key, CollectionKey = col.CollectionKey, AttemptId = Guid.NewGuid(), Score = 800, UnlockedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var before = await GetAtlas(c);
        Assert.Equal(19, before.Collections.Single(x => x.Key == collection.Key).Unlocked);
        Assert.False(before.Collections.Single(x => x.Key == collection.Key).CompletionXpEarned);

        var r = await Attempt(c, easy, ApiFactory.SolidJpeg(easy.Hex));
        Assert.NotNull(r.AtlasUnlock);
        Assert.True(r.AtlasUnlock!.CollectionCompleted);
        Assert.Equal(20, r.AtlasUnlock.CollectionUnlocked);
        Assert.Equal(500, r.AtlasUnlock.CompletionXpAwarded);
        Assert.Equal(100 + 500, r.TotalXp);

        var after = (await GetAtlas(c)).Collections.Single(x => x.Key == collection.Key);
        Assert.True(after.Completed);
        Assert.True(after.CompletionXpEarned);

        // replay: nothing more, and the ledger shows one completion entry
        var replay = await Attempt(c, easy, ApiFactory.SolidJpeg(easy.Hex));
        Assert.Null(replay.AtlasUnlock);
        Assert.Equal(600, replay.TotalXp);
        using var s2 = factory.Services.CreateScope();
        var db2 = s2.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db2.XpTransactions.CountAsync(t => t.UserId == auth.User.Id && t.SourceType == XpSourceType.AtlasCollectionComplete));
        var total = (await db2.Progress.AsNoTracking().FirstAsync(p => p.UserId == auth.User.Id)).TotalXp;
        Assert.Equal(total, await db2.XpTransactions.Where(t => t.UserId == auth.User.Id).SumAsync(t => t.Amount));
    }
}
