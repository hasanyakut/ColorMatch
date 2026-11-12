using System.Net;
using System.Net.Http.Json;
using ColorMatch.Application.DTOs;
using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.Domain.Entities;
using ColorMatch.Domain.Enums;
using ColorMatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ColorMatch.Tests.Integration;

[Collection("api")]
public class DailyHuntApiTests(ApiFactory factory)
{
    private static Task<HttpResponseMessage> Post(HttpClient c, int huntId, byte[] jpeg, double r = 0.1)
        => c.PostAsync($"/api/game/daily-hunts/{huntId}/attempts", ApiFactory.HuntForm(jpeg, 0.5, 0.5, r));

    private static async Task<DailyHuntAttemptResultDto> Attempt(HttpClient c, int huntId, byte[] jpeg)
    {
        var resp = await Post(c, huntId, jpeg);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<DailyHuntAttemptResultDto>())!;
    }

    private static Task<DailyHuntsDto> Today(HttpClient c) => c.GetFromJsonAsync<DailyHuntsDto>("/api/game/daily-hunts")!;

    private static byte[] Photo(DailyHuntDto hunt) => ApiFactory.SolidJpeg(hunt.Hex);

    /// <summary>A colour far from <paramref name="hex"/> (channels inverted): scores well below the completion threshold.</summary>
    private static byte[] Wrong(DailyHuntDto hunt)
    {
        var c = Rgb.FromHex(hunt.Hex);
        return ApiFactory.SolidJpeg(new Rgb((byte)(255 - c.R), (byte)(255 - c.G), (byte)(255 - c.B)).ToHex());
    }

    private static DailyHuntDto Of(DailyHuntsDto d, string difficulty) => d.Hunts.Single(h => h.Difficulty == difficulty);

    private async Task<(int Total, int Ledger)> Xp(Guid user)
    {
        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var total = (await db.Progress.AsNoTracking().FirstAsync(p => p.UserId == user)).TotalXp;
        var ledger = await db.XpTransactions.Where(t => t.UserId == user).SumAsync(t => (int?)t.Amount) ?? 0;
        return (total, ledger);
    }

    // ------------------------------------------------------------ the day's hunts
    [Fact]
    public async Task Every_player_sees_the_same_three_server_chosen_hunts()
    {
        var (a, _) = await factory.RegisterAsync();
        var (b, _) = await factory.RegisterAsync();

        var da = await Today(a);
        var db = await Today(b);

        Assert.Equal(new[] { "Easy", "Medium", "Hard" }, da.Hunts.Select(h => h.Difficulty));
        Assert.Equal(da.Hunts.Select(h => (h.Id, h.Hex)), db.Hunts.Select(h => (h.Id, h.Hex)));
        Assert.Equal(3, da.Hunts.Select(h => h.Hex).Distinct().Count());
        Assert.Equal(new[] { 100, 200, 350 }, da.Hunts.Select(h => h.BaseXp));
        Assert.All(da.Hunts, h => { Assert.Equal(700, h.MinScore); Assert.False(h.Completed); Assert.Equal(0, h.Attempts); });
        Assert.True(da.ResetsAtUtc > DateTime.UtcNow);

        // every hunt colour is a Color Atlas colour of the matching difficulty
        Assert.All(da.Hunts, h => Assert.Equal(h.Difficulty, ColorMatch.Domain.Rules.AtlasCatalog.FindByHex(h.Hex)!.Difficulty.ToString()));
    }

    [Fact]
    public async Task Hunt_endpoints_require_authentication()
    {
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/game/daily-hunts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/game/daily-hunts/progress")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync("/api/game/daily-hunts/1/attempts", ApiFactory.HuntForm(ApiFactory.SolidJpeg("#112233")))).StatusCode);
    }

    // ------------------------------------------------------------ completing a hunt
    [Fact]
    public async Task A_valid_attempt_completes_the_hunt_pays_its_xp_once_and_starts_the_streak()
    {
        var (c, auth) = await factory.RegisterAsync();
        var easy = Of(await Today(c), "Easy");

        var r = await Attempt(c, easy.Id, Photo(easy));
        Assert.True(r.Passed);
        Assert.True(r.Score >= 700);
        Assert.Equal(100, r.XpAwarded);
        Assert.True(r.FirstCompletion);
        Assert.Equal(1, r.CompletedCount);
        Assert.False(r.AllCompleted);
        Assert.Equal(100, r.TotalXp);
        Assert.Equal(2, r.Level);
        Assert.True(r.LeveledUp);
        Assert.Equal(1, r.CurrentStreak);
        Assert.True(r.StreakExtended);
        Assert.Equal(easy.Hex, r.TargetHex);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(r.PhotoUrl)).StatusCode);

        // a better or equal replay pays nothing more
        var replay = await Attempt(c, easy.Id, Photo(easy));
        Assert.True(replay.Passed);
        Assert.Equal(0, replay.XpAwarded);
        Assert.False(replay.FirstCompletion);
        Assert.Equal(100, replay.TotalXp);
        Assert.False(replay.StreakExtended);

        var day = await Today(c);
        var h = Of(day, "Easy");
        Assert.True(h.Completed);
        Assert.Equal(2, h.Attempts);
        Assert.Equal(100, h.XpEarned);
        Assert.NotNull(h.BestPhotoUrl);
        Assert.Equal(1, day.Progress.Completed);
        Assert.Equal((100, 100), await Xp(auth.User.Id));
    }

    [Fact]
    public async Task A_score_below_the_threshold_gives_no_xp_no_completion_and_no_streak()
    {
        var (c, auth) = await factory.RegisterAsync();
        var hard = Of(await Today(c), "Hard");

        var r = await Attempt(c, hard.Id, Wrong(hard));
        Assert.False(r.Passed);
        Assert.True(r.Score < 700);
        Assert.Equal(0, r.XpAwarded);
        Assert.False(r.FirstCompletion);
        Assert.Equal(0, r.CompletedCount);
        Assert.Equal(0, r.CurrentStreak);
        Assert.Equal("NeedsImprovement", r.Tier);
        Assert.Equal((0, 0), await Xp(auth.User.Id));

        // the best score is kept, the hunt stays open and can be retried
        var h = Of(await Today(c), "Hard");
        Assert.False(h.Completed);
        Assert.Equal(r.Score, h.BestScore);

        var ok = await Attempt(c, hard.Id, Photo(hard));
        Assert.True(ok.Passed);
        Assert.Equal(350, ok.XpAwarded);
        Assert.True(ok.IsNewBest);
        Assert.Equal(ok.Score, ok.BestScore);
    }

    [Fact]
    public async Task Completing_all_three_pays_the_bonus_exactly_once()
    {
        var (c, auth) = await factory.RegisterAsync();
        var day = await Today(c);

        var e = await Attempt(c, Of(day, "Easy").Id, Photo(Of(day, "Easy")));
        var m = await Attempt(c, Of(day, "Medium").Id, Photo(Of(day, "Medium")));
        Assert.Equal(0, e.BonusXpAwarded + m.BonusXpAwarded);
        Assert.False(m.AllCompleted);

        var h = await Attempt(c, Of(day, "Hard").Id, Photo(Of(day, "Hard")));
        Assert.True(h.AllCompleted);
        Assert.Equal(3, h.CompletedCount);
        Assert.Equal(350, h.XpAwarded);
        Assert.Equal(150, h.BonusXpAwarded);
        Assert.Equal(100 + 200 + 350 + 150, h.TotalXp);

        // playing on changes nothing
        var again = await Attempt(c, Of(day, "Hard").Id, Photo(Of(day, "Hard")));
        Assert.Equal(0, again.XpAwarded + again.BonusXpAwarded);
        Assert.Equal(800, again.TotalXp);

        var progress = (await c.GetFromJsonAsync<DailyProgressDto>("/api/game/daily-hunts/progress"))!;
        Assert.Equal((3, 3, true, true), (progress.Completed, progress.Total, progress.AllCompleted, progress.BonusEarned));
        Assert.Equal(800, progress.XpEarnedToday);
        Assert.Equal(800, progress.XpAvailableToday);

        Assert.Equal((800, 800), await Xp(auth.User.Id));
        using var s = factory.Services.CreateScope();
        var rows = await s.ServiceProvider.GetRequiredService<AppDbContext>().XpTransactions.Where(t => t.UserId == auth.User.Id).ToListAsync();
        Assert.Equal(4, rows.Count);   // 3 hunts + 1 bonus, nothing else
        Assert.Single(rows, t => t.SourceType == XpSourceType.DailyHuntCompletion);
    }

    [Fact]
    public async Task Parallel_valid_attempts_on_the_same_hunt_pay_the_xp_once()
    {
        var (c, auth) = await factory.RegisterAsync();
        var med = Of(await Today(c), "Medium");

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            var resp = await Post(c, med.Id, Photo(med));
            return resp.StatusCode == HttpStatusCode.OK ? await resp.Content.ReadFromJsonAsync<DailyHuntAttemptResultDto>() : null;
        }));

        var ok = results.Where(r => r is not null).Select(r => r!).ToList();
        Assert.NotEmpty(ok);
        Assert.Equal(1, ok.Count(r => r.FirstCompletion));          // exactly one attempt was the first completion
        Assert.Equal(200, ok.Sum(r => r.XpAwarded));                  // and exactly one XP payment
        Assert.Equal((200, 200), await Xp(auth.User.Id));
    }

    // ------------------------------------------------------------ rules that keep it honest
    [Fact]
    public async Task One_photo_cannot_complete_two_different_hunts()
    {
        var (c, _) = await factory.RegisterAsync();
        var day = await Today(c);
        var easy = Of(day, "Easy"); var medium = Of(day, "Medium");

        var photo = Photo(easy);
        Assert.True((await Attempt(c, easy.Id, photo)).Passed);

        var reuse = await Post(c, medium.Id, photo);
        Assert.Equal(HttpStatusCode.Conflict, reuse.StatusCode);
        Assert.False(Of(await Today(c), "Medium").Completed);
        Assert.Equal(0, Of(await Today(c), "Medium").Attempts);   // a rejected photo is not stored as an attempt
    }

    [Fact]
    public async Task Hunts_of_a_past_day_and_unknown_hunts_are_rejected()
    {
        var (c, _) = await factory.RegisterAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, 987_654, ApiFactory.SolidJpeg("#336699"))).StatusCode);

        int yesterdayId;
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var old = new DailyHunt { ChallengeDate = new DateOnly(2001, 1, 1), Difficulty = HuntDifficulty.Easy, Name = "Old", TargetColorHex = "#336699", BaseXp = 100, MinScore = 700 };
            db.DailyHunts.Add(old);
            await db.SaveChangesAsync();
            yesterdayId = old.Id;
        }
        var resp = await Post(c, yesterdayId, ApiFactory.SolidJpeg("#336699"));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task The_attempt_limit_per_hunt_is_enforced_by_the_server()
    {
        var (c, auth) = await factory.RegisterAsync();
        var easy = Of(await Today(c), "Easy");

        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 30; i++)
                db.DailyHuntAttempts.Add(new DailyHuntAttempt
                {
                    Id = Guid.NewGuid(), UserId = auth.User.Id, DailyHuntId = easy.Id, Score = 10, SampledColorHex = "#000000", PhotoKey = $"x/{i}",
                    PhotoHash = "h" + i, CreatedAtUtc = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        var resp = await Post(c, easy.Id, Photo(easy));
        Assert.Equal(HttpStatusCode.TooManyRequests, resp.StatusCode);
        Assert.Equal((0, 0), await Xp(auth.User.Id));
    }

    [Fact]
    public async Task Non_images_are_rejected_and_leave_no_trace()
    {
        var (c, auth) = await factory.RegisterAsync();
        var easy = Of(await Today(c), "Easy");

        var notImage = await c.PostAsync($"/api/game/daily-hunts/{easy.Id}/attempts", ApiFactory.HuntForm("not an image"u8.ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, notImage.StatusCode);

        Assert.Equal(0, Of(await Today(c), "Easy").Attempts);
        Assert.Equal((0, 0), await Xp(auth.User.Id));
    }

    [Fact]
    public async Task The_week_lists_seven_days_ending_today_and_counts_only_my_completed_hunts()
    {
        var (c, _) = await factory.RegisterAsync();
        var (other, _) = await factory.RegisterAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/game/daily-hunts/week")).StatusCode);

        var empty = (await c.GetFromJsonAsync<WeekDto>("/api/game/daily-hunts/week"))!;
        Assert.Equal(7, empty.Days.Count);
        Assert.True(empty.Days[^1].IsToday);
        Assert.Equal(6, empty.Days.Count(d => !d.IsToday));
        Assert.Equal(Enumerable.Range(0, 7).Select(i => empty.Days[0].Date.AddDays(i)), empty.Days.Select(d => d.Date));   // consecutive, oldest first
        Assert.All(empty.Days, d => Assert.Equal((0, 3), (d.Completed, d.Total)));
        Assert.Equal((0, 0), (empty.ActiveDays, empty.PerfectDays));

        var today = await Today(c);
        var easy = Of(today, "Easy");
        await Attempt(c, easy.Id, Photo(easy));
        await Attempt(c, easy.Id, ApiFactory.SolidJpeg(easy.Hex, 180));   // a second valid photo of the same hunt still counts once
        var one = (await c.GetFromJsonAsync<WeekDto>("/api/game/daily-hunts/week"))!;
        Assert.Equal(1, one.Days[^1].Completed);
        Assert.Equal((1, 0), (one.ActiveDays, one.PerfectDays));

        foreach (var d in new[] { "Medium", "Hard" }) { var h = Of(today, d); await Attempt(c, h.Id, Photo(h)); }
        var full = (await c.GetFromJsonAsync<WeekDto>("/api/game/daily-hunts/week"))!;
        Assert.Equal(3, full.Days[^1].Completed);
        Assert.Equal((1, 1), (full.ActiveDays, full.PerfectDays));

        // somebody else's week is untouched
        Assert.Equal(0, (await other.GetFromJsonAsync<WeekDto>("/api/game/daily-hunts/week"))!.ActiveDays);
    }

    [Fact]
    public async Task Players_only_see_and_change_their_own_hunt_state()
    {
        var (a, ua) = await factory.RegisterAsync();
        var (b, ub) = await factory.RegisterAsync();
        var easy = Of(await Today(a), "Easy");

        await Attempt(a, easy.Id, Photo(easy));

        var theirs = Of(await Today(b), "Easy");
        Assert.False(theirs.Completed);
        Assert.Equal(0, theirs.Attempts);
        Assert.Null(theirs.BestPhotoUrl);
        Assert.Equal((100, 100), await Xp(ua.User.Id));
        Assert.Equal((0, 0), await Xp(ub.User.Id));   // someone else's completion never touches my progress
    }

    // ------------------------------------------------------------ ranking
    [Fact]
    public async Task The_daily_ranking_adds_up_each_players_best_score_per_hunt()
    {
        var (a, ua) = await factory.RegisterAsync();
        var (b, ub) = await factory.RegisterAsync();
        var (idle, _) = await factory.RegisterAsync();
        var day = await Today(a);

        var a1 = await Attempt(a, Of(day, "Easy").Id, Photo(Of(day, "Easy")));
        var a2 = await Attempt(a, Of(day, "Medium").Id, Photo(Of(day, "Medium")));
        var b1 = await Attempt(b, Of(day, "Easy").Id, Photo(Of(day, "Easy")));

        var board = (await a.GetFromJsonAsync<LeaderboardDto>("/api/game/daily-hunts/leaderboard?take=50"))!;
        var me = board.Entries.Single(e => e.UserId == ua.User.Id);
        var other = board.Entries.Single(e => e.UserId == ub.User.Id);
        Assert.Equal(a1.Score + a2.Score, me.BestScore);
        Assert.Equal(b1.Score, other.BestScore);
        Assert.True(me.Rank < other.Rank);
        Assert.True(me.IsMe);
        Assert.True(board.TotalPlayers >= 2);

        var idleBoard = (await idle.GetFromJsonAsync<LeaderboardDto>("/api/game/daily-hunts/leaderboard"))!;
        Assert.Null(idleBoard.MyRank);   // not playing = not ranked
    }
}
