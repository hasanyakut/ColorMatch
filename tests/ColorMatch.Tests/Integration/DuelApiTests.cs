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
public class DuelApiTests(ApiFactory factory)
{
    private sealed record Player(HttpClient Http, Guid Id);

    private async Task<Player> NewPlayer()
    {
        var (c, a) = await factory.RegisterAsync();
        return new Player(c, a.User.Id);
    }

    private async Task<(Player A, Player B)> Friends()
    {
        var a = await NewPlayer(); var b = await NewPlayer();
        await a.Http.PostAsJsonAsync("/api/friends/requests", new SendFriendRequestBody(b.Id));
        Assert.Equal(HttpStatusCode.OK, (await b.Http.PostAsync($"/api/friends/requests/{a.Id}/accept", null)).StatusCode);
        return (a, b);
    }

    private static async Task<DuelDto> Read(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<DuelDto>())!;
    }

    private static Task<DuelDto> Challenge(Player from, Player to) => from.Http.PostAsJsonAsync("/api/game/duels", new CreateDuelRequest(to.Id)).ContinueWith(t => Read(t.Result)).Unwrap();
    private static Task<DuelDto> Get(Player p, Guid id) => p.Http.GetFromJsonAsync<DuelDto>($"/api/game/duels/{id}")!;

    private async Task<(Player A, Player B, DuelDto Duel)> ActiveDuel()
    {
        var (a, b) = await Friends();
        var d = await Challenge(a, b);
        await Read(await b.Http.PostAsync($"/api/game/duels/{d.Id}/accept", null));
        return (a, b, await Get(a, d.Id));
    }

    private static Task<HttpResponseMessage> Play(Player p, Guid duel, byte[] jpeg)
        => p.Http.PostAsync($"/api/game/duels/{duel}/attempts", ApiFactory.HuntForm(jpeg, 0.5, 0.5, 0.1));

    private static byte[] Exact(string hex) => ApiFactory.SolidJpeg(hex);
    /// <summary>Distinct photos that are all far from the target: shades of black or white, whichever is farther.</summary>
    private static byte[] Far(string hex, int step)
    {
        var c = Rgb.FromHex(hex);
        var v = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 128 ? step * 12 : 255 - step * 12;
        return ApiFactory.SolidJpeg($"#{v:X2}{v:X2}{v:X2}");
    }
    private static byte[] Wrong(string hex)
    {
        var c = Rgb.FromHex(hex);
        return ApiFactory.SolidJpeg((0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 128 ? "#000000" : "#FFFFFF");
    }

    private async Task<(int Total, int Ledger, int DuelRows)> Xp(Guid user)
    {
        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var total = (await db.Progress.AsNoTracking().FirstAsync(p => p.UserId == user)).TotalXp;
        var ledger = await db.XpTransactions.Where(t => t.UserId == user).SumAsync(t => (int?)t.Amount) ?? 0;
        var rows = await db.XpTransactions.CountAsync(t => t.UserId == user && t.SourceType == XpSourceType.DuelResult);
        return (total, ledger, rows);
    }

    /// <summary>Seeds an active duel directly, with the given best scores and deadline (the API cannot move the clock).</summary>
    private async Task<Guid> Seed(Player a, Player b, DuelStatus status, DateTime expires, int? aScore, int? bScore, bool accepted = true)
    {
        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var color = AtlasCatalog.All[3];
        var now = DateTime.UtcNow;
        var d = new Duel
        {
            Id = Guid.NewGuid(), ChallengerId = a.Id, OpponentId = b.Id, ColorKey = color.Key, Status = status, CreatedAtUtc = now.AddHours(-30),
            UpdatedAtUtc = now.AddHours(-30), AcceptedAtUtc = accepted ? now.AddHours(-26) : null, ExpiresAtUtc = expires,
        };
        db.Duels.Add(d);
        if (aScore is { } x) db.DuelAttempts.Add(new DuelAttempt { Id = Guid.NewGuid(), DuelId = d.Id, UserId = a.Id, Score = x, SampledColorHex = "#112233", PhotoHash = Guid.NewGuid().ToString("N"), CreatedAtUtc = now.AddHours(-25) });
        if (bScore is { } y) db.DuelAttempts.Add(new DuelAttempt { Id = Guid.NewGuid(), DuelId = d.Id, UserId = b.Id, Score = y, SampledColorHex = "#112233", PhotoHash = Guid.NewGuid().ToString("N"), CreatedAtUtc = now.AddHours(-25) });
        await db.SaveChangesAsync();
        return d.Id;
    }

    [Fact]
    public async Task Duel_endpoints_require_authentication()
    {
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/game/duels")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/game/duels", new CreateDuelRequest(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task Only_friends_can_be_challenged_and_not_yourself()
    {
        var a = await NewPlayer(); var b = await NewPlayer();
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Http.PostAsJsonAsync("/api/game/duels", new CreateDuelRequest(b.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Http.PostAsJsonAsync("/api/game/duels", new CreateDuelRequest(a.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Http.PostAsJsonAsync("/api/game/duels", new CreateDuelRequest(Guid.NewGuid()))).StatusCode);

        await a.Http.PostAsJsonAsync("/api/friends/requests", new SendFriendRequestBody(b.Id));   // still only a request
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Http.PostAsJsonAsync("/api/game/duels", new CreateDuelRequest(b.Id))).StatusCode);
    }

    [Fact]
    public async Task An_invitation_hides_the_colour_from_the_invited_player_until_they_accept()
    {
        var (a, b) = await Friends();
        var d = await Challenge(a, b);

        Assert.Equal(("Pending", "challenger"), (d.Status, d.Role));
        Assert.NotNull(d.TargetHex);
        var theirs = await Get(b, d.Id);
        Assert.Equal("opponent", theirs.Role);
        Assert.True(theirs.NeedsMyResponse);
        Assert.Null(theirs.TargetHex);
        Assert.Null(theirs.TargetName);
        Assert.False(theirs.CanPlay);

        // a second duel with the same friend is refused while one is open
        Assert.Equal(HttpStatusCode.Conflict, (await a.Http.PostAsJsonAsync("/api/game/duels", new CreateDuelRequest(b.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Play(a, d.Id, Exact(d.TargetHex!))).StatusCode);   // not accepted yet

        var accepted = await Read(await b.Http.PostAsync($"/api/game/duels/{d.Id}/accept", null));
        Assert.Equal("Active", accepted.Status);
        Assert.Equal(d.TargetHex, accepted.TargetHex);
        Assert.True(accepted.CanPlay);
        Assert.InRange((accepted.ExpiresAtUtc - DateTime.UtcNow).TotalHours, 23.9, 24.1);
    }

    [Fact]
    public async Task Only_the_invited_player_can_accept_and_strangers_see_nothing()
    {
        var (a, b) = await Friends();
        var stranger = await NewPlayer();
        var d = await Challenge(a, b);

        Assert.Equal(HttpStatusCode.Forbidden, (await a.Http.PostAsync($"/api/game/duels/{d.Id}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync($"/api/game/duels/{d.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.PostAsync($"/api/game/duels/{d.Id}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Play(stranger, d.Id, Exact("#112233"))).StatusCode);
        Assert.Empty((await stranger.Http.GetFromJsonAsync<DuelListDto>("/api/game/duels"))!.Duels);
    }

    [Fact]
    public async Task A_stranger_cannot_decline_finish_or_play_someone_elses_duel_and_leaves_it_untouched()
    {
        var (a, b, d) = await ActiveDuel();
        var stranger = await NewPlayer();
        await Play(a, d.Id, Exact(d.TargetHex!));

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.PostAsync($"/api/game/duels/{d.Id}/decline", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.PostAsync($"/api/game/duels/{d.Id}/finish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Play(stranger, d.Id, Exact(d.TargetHex!))).StatusCode);

        var after = await Get(a, d.Id);
        Assert.Equal(("Active", false, 2), (after.Status, after.MyDone, after.MyAttemptsLeft));
        Assert.Equal(1, (await Get(b, d.Id)).OtherAttempts);
        Assert.Equal(0, (await Xp(stranger.Id)).DuelRows);
    }

    [Fact]
    public async Task An_invitation_can_be_declined_or_withdrawn_and_frees_the_pair()
    {
        var (a, b) = await Friends();
        var d1 = await Challenge(a, b);
        Assert.Equal("Declined", (await Read(await b.Http.PostAsync($"/api/game/duels/{d1.Id}/decline", null))).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await b.Http.PostAsync($"/api/game/duels/{d1.Id}/accept", null)).StatusCode);

        var d2 = await Challenge(a, b);   // allowed again
        Assert.Equal("Declined", (await Read(await a.Http.PostAsync($"/api/game/duels/{d2.Id}/decline", null))).Status);   // challenger withdraws
    }

    [Fact]
    public async Task A_full_duel_gives_the_winner_200_xp_and_the_loser_50_and_hides_scores_until_it_is_over()
    {
        var (a, b, d) = await ActiveDuel();
        var target = d.TargetHex!;
        var before = (await Xp(a.Id), await Xp(b.Id));

        // challenger plays all three attempts: exact colour on the first, then different (but valid) photos
        var r1 = (await (await Play(a, d.Id, Exact(target))).Content.ReadFromJsonAsync<DuelAttemptResultDto>())!;
        Assert.True(r1.Score >= 900);
        Assert.Equal(2, r1.AttemptsLeft);
        Assert.False(r1.Finished);
        Assert.Equal(HttpStatusCode.OK, (await Play(a, d.Id, Wrong(target))).StatusCode);
        var r3 = (await (await Play(a, d.Id, ApiFactory.SolidJpeg("#808080"))).Content.ReadFromJsonAsync<DuelAttemptResultDto>())!;
        Assert.True(r3.Finished);
        Assert.False(r3.DuelResolved);
        Assert.Equal(r1.Score, r3.MyBest);

        // a fourth attempt is not possible
        Assert.Equal(HttpStatusCode.Conflict, (await Play(a, d.Id, Wrong(target))).StatusCode);

        // while the opponent has not finished, they see only that the challenger played, never the score
        var seenByB = await Get(b, d.Id);
        Assert.Equal(3, seenByB.OtherAttempts);
        Assert.True(seenByB.OtherDone);
        Assert.Null(seenByB.OtherBest);
        Assert.Null(seenByB.Outcome);
        Assert.Equal("Active", seenByB.Status);

        await Play(b, d.Id, Far(target, 3));
        await Play(b, d.Id, Far(target, 4));
        var lastResp = await Play(b, d.Id, Far(target, 5));
        Assert.True(lastResp.IsSuccessStatusCode, await lastResp.Content.ReadAsStringAsync());
        var last = (await lastResp.Content.ReadFromJsonAsync<DuelAttemptResultDto>())!;
        Assert.True(last.DuelResolved);
        Assert.Equal("lose", last.Duel.Outcome);
        Assert.Equal(50, last.XpAwarded);

        var doneA = await Get(a, d.Id);
        Assert.Equal(("Completed", "win", 200), (doneA.Status, doneA.Outcome, doneA.MyXp));
        Assert.NotNull(doneA.OtherBest);
        var doneB = await Get(b, d.Id);
        Assert.Equal(("lose", 50), (doneB.Outcome, doneB.MyXp));
        Assert.Equal(r1.Score, doneB.OtherBest);

        var afterA = await Xp(a.Id); var afterB = await Xp(b.Id);
        Assert.Equal(before.Item1.Total + 200, afterA.Total);
        Assert.Equal(before.Item2.Total + 50, afterB.Total);
        Assert.Equal(afterA.Total, afterA.Ledger);   // ledger and total stay in step
        Assert.Equal(afterB.Total, afterB.Ledger);

        // finished duels cannot be played any more, and read again they pay nothing more
        Assert.Equal(HttpStatusCode.Conflict, (await Play(a, d.Id, Wrong(target))).StatusCode);
        await Get(a, d.Id); await Get(b, d.Id);
        Assert.Equal(afterA, await Xp(a.Id));
    }

    [Fact]
    public async Task Finishing_early_keeps_the_best_score_and_settles_when_both_are_done()
    {
        var (a, b, d) = await ActiveDuel();
        await Play(a, d.Id, Exact(d.TargetHex!));
        await Play(b, d.Id, Wrong(d.TargetHex!));
        var fa = await Read(await a.Http.PostAsync($"/api/game/duels/{d.Id}/finish", null));
        Assert.True(fa.MyDone);
        Assert.Equal("Active", fa.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Play(a, d.Id, Wrong(d.TargetHex!))).StatusCode);   // done is done
        var fb = await Read(await b.Http.PostAsync($"/api/game/duels/{d.Id}/finish", null));
        Assert.Equal(("Completed", "lose"), (fb.Status, fb.Outcome));
        Assert.True(fb.OtherBest >= 900);
    }

    [Fact]
    public async Task The_same_photo_cannot_be_played_twice_in_one_duel()
    {
        var (a, b, d) = await ActiveDuel();
        var photo = Exact(d.TargetHex!);
        Assert.Equal(HttpStatusCode.OK, (await Play(a, d.Id, photo)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Play(a, d.Id, photo)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Play(b, d.Id, photo)).StatusCode);   // copying the other player's photo
    }

    [Fact]
    public async Task Overdue_duel_is_settled_lazily_and_a_player_who_never_played_forfeits()
    {
        var (a, b) = await Friends();
        var id = await Seed(a, b, DuelStatus.Active, DateTime.UtcNow.AddMinutes(-1), aScore: 640, bScore: null);

        var seen = await Get(b, id);   // the settle happens on this read, no timer involved
        Assert.Equal(("Expired", "lose", 0), (seen.Status, seen.Outcome, seen.MyXp));
        Assert.Equal(640, seen.OtherBest);
        var mine = await Get(a, id);
        Assert.Equal(("win", 200), (mine.Outcome, mine.MyXp));
        Assert.Equal((200, 0), ((await Xp(a.Id)).Total, (await Xp(b.Id)).DuelRows));
        Assert.Equal(HttpStatusCode.Conflict, (await Play(b, id, Exact("#112233"))).StatusCode);
    }

    [Fact]
    public async Task Overdue_duel_with_equal_best_scores_is_a_draw_for_100_xp_each()
    {
        var (a, b) = await Friends();
        var id = await Seed(a, b, DuelStatus.Active, DateTime.UtcNow.AddMinutes(-5), 800, 800);
        var da = (await a.Http.GetFromJsonAsync<DuelListDto>("/api/game/duels"))!.Duels.Single(x => x.Id == id);
        Assert.Equal(("Expired", "draw", 100), (da.Status, da.Outcome, da.MyXp));
        Assert.Equal(("draw", 100), ((await Get(b, id)).Outcome, (await Get(b, id)).MyXp));
    }

    [Fact]
    public async Task Overdue_duel_nobody_played_and_unanswered_invitations_just_expire_without_xp()
    {
        var (a, b) = await Friends();
        var idle = await Seed(a, b, DuelStatus.Active, DateTime.UtcNow.AddMinutes(-1), null, null);
        var d = await Get(a, idle);
        Assert.Equal(("Expired", "none", 0), (d.Status, d.Outcome, d.MyXp));
        Assert.Equal(0, (await Xp(a.Id)).DuelRows);

        var unanswered = await Seed(a, b, DuelStatus.Pending, DateTime.UtcNow.AddMinutes(-1), null, null, accepted: false);
        var p = await Get(b, unanswered);
        Assert.Equal("Expired", p.Status);
        Assert.Null(p.Outcome);
        Assert.Equal(HttpStatusCode.Conflict, (await b.Http.PostAsync($"/api/game/duels/{unanswered}/accept", null)).StatusCode);
    }

    [Fact]
    public async Task Parallel_reads_settle_an_overdue_duel_and_pay_it_exactly_once()
    {
        var (a, b) = await Friends();
        var id = await Seed(a, b, DuelStatus.Active, DateTime.UtcNow.AddMinutes(-1), 900, 500);

        var calls = Enumerable.Range(0, 8).Select(i => i % 2 == 0 ? a.Http.GetAsync($"/api/game/duels/{id}") : b.Http.GetAsync($"/api/game/duels")).ToArray();
        var responses = await Task.WhenAll(calls);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var xa = await Xp(a.Id); var xb = await Xp(b.Id);
        Assert.Equal((200, 1), (xa.Total, xa.DuelRows));
        Assert.Equal((50, 1), (xb.Total, xb.DuelRows));
        Assert.Equal(xa.Total, xa.Ledger);
        Assert.Equal(xb.Total, xb.Ledger);
    }

    [Fact]
    public async Task Duel_xp_is_capped_per_day()
    {
        var (a, b) = await Friends();
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var progress = await db.Progress.FirstAsync(p => p.UserId == a.Id);
            progress.TotalXp += 550;
            db.XpTransactions.Add(new XpTransaction
            {
                Id = Guid.NewGuid(), UserId = a.Id, Amount = 550, SourceType = XpSourceType.DuelResult, Reason = "seed", IdempotencyKey = "seed:" + Guid.NewGuid().ToString("N"),
                BalanceAfter = progress.TotalXp, CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var id = await Seed(a, b, DuelStatus.Active, DateTime.UtcNow.AddMinutes(-1), 900, 100);
        var d = await Get(a, id);
        Assert.Equal(("win", 50), (d.Outcome, d.MyXp));   // 600 cap − 550 already earned today
        var xa = await Xp(a.Id);
        Assert.Equal(xa.Total, xa.Ledger);
        Assert.Equal(900, (await Get(b, id)).OtherBest);
        Assert.Equal(50, (await Xp(b.Id)).Total);
    }

    [Fact]
    public async Task The_number_of_open_duels_is_limited()
    {
        var host = await NewPlayer();
        for (var i = 0; i < 5; i++)
        {
            var f = await NewPlayer();
            await host.Http.PostAsJsonAsync("/api/friends/requests", new SendFriendRequestBody(f.Id));
            await f.Http.PostAsync($"/api/friends/requests/{host.Id}/accept", null);
            Assert.Equal(HttpStatusCode.OK, (await host.Http.PostAsJsonAsync("/api/game/duels", new CreateDuelRequest(f.Id))).StatusCode);
        }
        var extra = await NewPlayer();
        await host.Http.PostAsJsonAsync("/api/friends/requests", new SendFriendRequestBody(extra.Id));
        await extra.Http.PostAsync($"/api/friends/requests/{host.Id}/accept", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.Http.PostAsJsonAsync("/api/game/duels", new CreateDuelRequest(extra.Id))).StatusCode);
    }

    [Fact]
    public async Task The_list_puts_invitations_first_and_reports_the_balance()
    {
        var (a, b) = await Friends();
        var d = await Challenge(a, b);
        var list = (await b.Http.GetFromJsonAsync<DuelListDto>("/api/game/duels"))!;
        Assert.Single(list.Duels);
        Assert.True(list.Duels[0].NeedsMyResponse);
        Assert.Equal((1, 5, 200, 100, 50), (list.OpenCount, list.MaxOpen, list.WinXp, list.DrawXp, list.LoseXp));
        Assert.Equal(d.Id, list.Duels[0].Id);
    }
}
