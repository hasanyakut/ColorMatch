using System.Net;
using System.Net.Http.Json;
using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using ColorMatch.Application.Services;
using ColorMatch.Domain.Entities;
using ColorMatch.Domain.Enums;
using ColorMatch.Domain.Rules;
using ColorMatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ColorMatch.Tests.Integration;

[Collection("api")]
public class GameCoreApiTests(ApiFactory factory)
{
    // ---------- helpers ----------
    private IServiceScope Scope() => factory.Services.CreateScope();

    private static RewardRequest Reward(Guid user, string key, int amount = 100, XpSourceType src = XpSourceType.Achievement)
        => new(user, amount, src, null, "test reward", key);

    private async Task<(int Total, int Ledger, int Level)> Totals(Guid user)
    {
        using var s = Scope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var p = await db.Progress.AsNoTracking().FirstAsync(x => x.UserId == user);
        var ledger = await db.XpTransactions.Where(t => t.UserId == user).SumAsync(t => (int?)t.Amount) ?? 0;
        return (p.TotalXp, ledger, p.Level);
    }

    /// <summary>A challenge service whose "today" the test controls (the real clock cannot be moved).</summary>
    private sealed class FixedDayChallenges(DateOnly day) : IChallengeService
    {
        public DateOnly Day { get; set; } = day;
        public DateOnly TodayLocal() => Day;
        public DateTime NextResetUtc(DateOnly date) => throw new NotSupportedException();
        public Task<DailyChallengeDto> GetTodayAsync(Guid? userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<DailyColorChallenge> GetOrCreateTodayEntityAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<LeaderboardDto> GetLeaderboardAsync(Guid userId, int take, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PastChallengeDto>> GetRecentAsync(Guid userId, int days, CancellationToken ct) => throw new NotSupportedException();
    }

    // ---------- the XP ledger ----------
    [Fact]
    public async Task A_reward_books_one_ledger_row_and_raises_total_and_level()
    {
        var (_, auth) = await factory.RegisterAsync();
        using var s = Scope();
        var rewards = s.ServiceProvider.GetRequiredService<IRewardService>();

        var r = await rewards.GrantAndSaveAsync(Reward(auth.User.Id, "t:levelup:" + auth.User.Id, 450));
        Assert.True(r.Granted);
        Assert.Equal(450, r.TotalXp);
        Assert.True(r.LeveledUp);
        Assert.Equal(1, r.PreviousLevel);
        Assert.Equal(3, r.Level);          // 400 XP starts level 3
        Assert.False(r.TitleChanged);       // still "Color Beginner"

        var t = await Totals(auth.User.Id);
        Assert.Equal((450, 450, 3), t);
    }

    [Fact]
    public async Task Reaching_level_five_changes_the_title()
    {
        var (_, auth) = await factory.RegisterAsync();
        using var s = Scope();
        var rewards = s.ServiceProvider.GetRequiredService<IRewardService>();
        var r = await rewards.GrantAndSaveAsync(Reward(auth.User.Id, "t:title:" + auth.User.Id, 1600));   // level 5 starts at 1600 XP
        Assert.Equal(5, r.Level);
        Assert.True(r.TitleChanged);
        Assert.Equal("Color Seeker", r.Title);
    }

    [Fact]
    public async Task The_same_idempotency_key_never_pays_twice()
    {
        var (_, auth) = await factory.RegisterAsync();
        var key = "t:once:" + auth.User.Id;

        using (var s = Scope())
        {
            var rewards = s.ServiceProvider.GetRequiredService<IRewardService>();
            Assert.True((await rewards.GrantAndSaveAsync(Reward(auth.User.Id, key, 100))).Granted);
        }
        using (var s = Scope())
        {
            var rewards = s.ServiceProvider.GetRequiredService<IRewardService>();
            var again = await rewards.GrantAndSaveAsync(Reward(auth.User.Id, key, 100));
            Assert.False(again.Granted);
            Assert.Equal(100, again.TotalXp);
        }
        Assert.Equal((100, 100, 2), await Totals(auth.User.Id));
    }

    [Fact]
    public async Task Parallel_requests_with_the_same_key_pay_exactly_once()
    {
        var (_, auth) = await factory.RegisterAsync();
        var key = "t:race:" + auth.User.Id;

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var s = Scope();
            try { return (await s.ServiceProvider.GetRequiredService<IRewardService>().GrantAndSaveAsync(Reward(auth.User.Id, key, 100))).Granted; }
            catch (ConflictException) { return false; }
        }));

        Assert.Equal(1, results.Count(g => g));
        Assert.Equal((100, 100, 2), await Totals(auth.User.Id));
    }

    [Fact]
    public async Task Parallel_rewards_keep_total_xp_equal_to_the_ledger()
    {
        var (_, auth) = await factory.RegisterAsync();

        var granted = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            using var s = Scope();
            try
            {
                var r = await s.ServiceProvider.GetRequiredService<IRewardService>().GrantAndSaveAsync(Reward(auth.User.Id, $"t:par:{auth.User.Id}:{i}", 10));
                return r.Granted ? 10 : 0;
            }
            catch (ConflictException) { return 0; }   // allowed under heavy contention; what must hold is consistency
        }));

        var t = await Totals(auth.User.Id);
        Assert.Equal(granted.Sum(), t.Total);   // every XP the callers were told about is in the total …
        Assert.Equal(t.Total, t.Ledger);        // … and the total is exactly the ledger
        Assert.True(t.Total >= 10);
    }

    [Fact]
    public async Task Invalid_rewards_are_rejected()
    {
        var (_, auth) = await factory.RegisterAsync();
        using var s = Scope();
        var rewards = s.ServiceProvider.GetRequiredService<IRewardService>();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => rewards.GrantAsync(Reward(auth.User.Id, "t:neg", -5), default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => rewards.GrantAsync(Reward(auth.User.Id, "t:zero", 0), default));
        await Assert.ThrowsAsync<ArgumentException>(() => rewards.GrantAsync(Reward(auth.User.Id, "", 5), default));
        await Assert.ThrowsAsync<NotFoundException>(() => rewards.GrantAsync(Reward(Guid.NewGuid(), "t:ghost", 5), default));
        Assert.Equal((0, 0, 1), await Totals(auth.User.Id));
    }

    [Fact]
    public async Task A_colour_hunt_pays_through_the_ledger_and_replays_add_nothing()
    {
        var (client, auth) = await factory.RegisterAsync();
        var today = (await client.GetFromJsonAsync<DailyChallengeDto>("/api/challenges/today"))!;

        var first = (await (await client.PostAsync("/api/color-hunts", ApiFactory.HuntForm(ApiFactory.SolidJpeg(today.Hex)))).Content.ReadFromJsonAsync<HuntAttemptResultDto>())!;
        await client.PostAsync("/api/color-hunts", ApiFactory.HuntForm(ApiFactory.SolidJpeg(today.Hex)));   // replay

        var t = await Totals(auth.User.Id);
        Assert.Equal(first.TotalXp, t.Total);
        Assert.Equal(t.Total, t.Ledger);
        Assert.Equal(first.Level, t.Level);

        using var s = Scope();
        var rows = await s.ServiceProvider.GetRequiredService<AppDbContext>().XpTransactions.Where(x => x.UserId == auth.User.Id).ToListAsync();
        var row = Assert.Single(rows);   // the replay paid 0 → no second row
        Assert.Equal(XpSourceType.HuntAttempt, row.SourceType);
        Assert.StartsWith("hunt-attempt:", row.IdempotencyKey);
    }

    // ---------- the API ----------
    [Fact]
    public async Task Progression_endpoint_reports_level_title_and_progress()
    {
        var (client, auth) = await factory.RegisterAsync();
        using (var s = Scope())
            await s.ServiceProvider.GetRequiredService<IRewardService>().GrantAndSaveAsync(Reward(auth.User.Id, "t:api:" + auth.User.Id, 250));

        var p = (await client.GetFromJsonAsync<ProgressionDto>("/api/game/progression/me"))!;
        Assert.Equal(250, p.TotalXp);
        Assert.Equal(2, p.Level);
        Assert.Equal("Color Beginner", p.Title);
        Assert.Equal("Color Seeker", p.NextTitle);
        Assert.Equal(5, p.NextTitleLevel);
        Assert.Equal(100, p.LevelStartXp);
        Assert.Equal(400, p.NextLevelXp);
        Assert.Equal(150, p.XpIntoLevel);
        Assert.Equal(150, p.XpToNextLevel);
        Assert.Equal(0.5, p.Progress, 3);
    }

    [Fact]
    public async Task History_lists_only_my_own_transactions_newest_first_with_paging()
    {
        var (a, ua) = await factory.RegisterAsync();
        var (b, ub) = await factory.RegisterAsync();
        using (var s = Scope())
        {
            var rewards = s.ServiceProvider.GetRequiredService<IRewardService>();
            for (var i = 1; i <= 5; i++) await rewards.GrantAndSaveAsync(Reward(ua.User.Id, $"t:hist:{ua.User.Id}:{i}", i));
            await rewards.GrantAndSaveAsync(Reward(ub.User.Id, "t:hist:other:" + ub.User.Id, 999));
        }

        var page1 = (await a.GetFromJsonAsync<PagedResult<XpTransactionDto>>("/api/game/progression/history?pageSize=3"))!;
        Assert.Equal(3, page1.Items.Count);
        Assert.NotNull(page1.NextCursor);
        Assert.Equal(new[] { 5, 4, 3 }, page1.Items.Select(i => i.Amount));
        Assert.Equal(15, page1.Items[0].BalanceAfter);

        var page2 = (await a.GetFromJsonAsync<PagedResult<XpTransactionDto>>($"/api/game/progression/history?pageSize=3&cursor={Uri.EscapeDataString(page1.NextCursor!)}"))!;
        Assert.Equal(new[] { 2, 1 }, page2.Items.Select(i => i.Amount));
        Assert.Null(page2.NextCursor);

        var theirs = (await b.GetFromJsonAsync<PagedResult<XpTransactionDto>>("/api/game/progression/history"))!;
        Assert.Equal(999, Assert.Single(theirs.Items).Amount);   // nobody sees someone else's ledger
    }

    [Fact]
    public async Task Progression_endpoints_require_authentication_and_accept_no_client_xp()
    {
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/game/progression/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/game/progression/history")).StatusCode);

        // there is deliberately no write endpoint: clients cannot post XP or levels
        var (client, _) = await factory.RegisterAsync();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/api/game/progression/me", new { totalXp = 99999 })).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsJsonAsync("/api/game/progression/me", new { level = 50 })).StatusCode);
    }

    // ---------- streak (stored, server-side) ----------
    [Fact]
    public async Task Streak_is_stored_extends_by_day_and_resets_after_a_gap()
    {
        var (_, auth) = await factory.RegisterAsync();
        var day = new DateOnly(2030, 5, 1);
        var challenges = new FixedDayChallenges(day);

        async Task<StreakUpdate> Record(DateOnly d)
        {
            challenges.Day = d;
            using var s = Scope();
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var svc = new ProgressionService(db, challenges, s.ServiceProvider.GetRequiredService<LevelCurve>());
            var u = await svc.RecordQualifyingActivityAsync(auth.User.Id, default);
            await db.SaveChangesAsync();
            return u;
        }

        Assert.Equal(1, (await Record(day)).Current);
        Assert.False((await Record(day)).Extended);                // same day: counted once
        Assert.Equal(1, (await Record(day)).Current);
        Assert.Equal(2, (await Record(day.AddDays(1))).Current);
        Assert.Equal(3, (await Record(day.AddDays(2))).Current);
        var gap = await Record(day.AddDays(5));                    // two missed days
        Assert.Equal(1, gap.Current);
        Assert.Equal(3, gap.Longest);

        using var s2 = Scope();
        var p = await s2.ServiceProvider.GetRequiredService<AppDbContext>().Progress.AsNoTracking().FirstAsync(x => x.UserId == auth.User.Id);
        Assert.Equal((1, 3, (DateOnly?)day.AddDays(5)), (p.CurrentStreak, p.LongestStreak, p.LastQualifyingActivityDate));
    }

    [Fact]
    public async Task Streak_shown_to_the_player_expires_after_the_grace_day()
    {
        var (_, auth) = await factory.RegisterAsync();
        using var s = Scope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var p = await db.Progress.FirstAsync(x => x.UserId == auth.User.Id);
        var curve = s.ServiceProvider.GetRequiredService<LevelCurve>();
        p.CurrentStreak = 6; p.LongestStreak = 9; p.LastQualifyingActivityDate = new DateOnly(2030, 5, 1);

        var svc = new ProgressionService(db, new FixedDayChallenges(new DateOnly(2030, 5, 2)), curve);
        var dto = svc.Describe(p, new DateOnly(2030, 5, 2));
        Assert.Equal(6, dto.CurrentStreak);
        Assert.True(dto.StreakAtRisk);                   // alive, but today is not played yet

        var gone = svc.Describe(p, new DateOnly(2030, 5, 3));
        Assert.Equal(0, gone.CurrentStreak);             // a whole day was missed
        Assert.Equal(9, gone.LongestStreak);
        Assert.False(gone.StreakAtRisk);
    }

    // ---------- upgrade of existing players ----------
    [Fact]
    public async Task Backfill_converts_pre_ledger_xp_into_one_legacy_entry_and_is_idempotent()
    {
        var (_, auth) = await factory.RegisterAsync();
        using (var s = Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var p = await db.Progress.FirstAsync(x => x.UserId == auth.User.Id);
            p.TotalXp = 777;   // as if earned before the ledger existed
            await db.SaveChangesAsync();
        }

        for (var run = 0; run < 3; run++)   // running it again must change nothing
        {
            using var s = Scope();
            await GameCoreBackfill.RunAsync(s.ServiceProvider.GetRequiredService<AppDbContext>(), new GameBalance(), TimeProvider.System);
        }

        var t = await Totals(auth.User.Id);
        Assert.Equal((777, 777, 3), t);   // level 3 starts at 400 XP, level 4 at 900
        using var s2 = Scope();
        var row = Assert.Single(await s2.ServiceProvider.GetRequiredService<AppDbContext>().XpTransactions.Where(x => x.UserId == auth.User.Id).ToListAsync());
        Assert.Equal(XpSourceType.LegacyImport, row.SourceType);
    }
}
