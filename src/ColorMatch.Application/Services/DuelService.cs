using System.Security.Cryptography;
using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using ColorMatch.Domain.Entities;
using ColorMatch.Domain.Enums;
using ColorMatch.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ColorMatch.Application.Services;

/// <summary>
/// Friend duels. Two accepted friends photograph the same target colour (3 attempts each, 24 h after acceptance); the higher best score wins.
/// Nothing runs in the background: whenever a player's duels are read or played, overdue ones are settled first (lazy expiry). Resolution
/// (status, winner, XP) is one unit of work guarded by a row version and idempotency keys, so a duel is paid exactly once however many
/// requests race to settle it. The opponent's score stays hidden until the duel is over.
/// </summary>
public sealed class DuelService(
    IAppDbContext db,
    IColorMatchService colorMatch,
    IRewardService rewards,
    LevelCurve curve,
    IOptions<GameBalance> balanceOptions,
    TimeProvider clock) : IDuelService
{
    private const int Retries = 5;
    private const int ListSize = 50;
    private DuelBalance Balance => balanceOptions.Value.Duel;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public static string RewardKey(Guid duelId, Guid userId) => $"duel:{duelId:N}:{userId:N}";

    // ------------------------------------------------------------------ reads
    public async Task<DuelListDto> ListAsync(Guid userId, CancellationToken ct)
    {
        await SettleOverdueAsync(userId, ct);
        var duels = await db.Duels.AsNoTracking().Include(d => d.Challenger).Include(d => d.Opponent)
            .Where(d => d.ChallengerId == userId || d.OpponentId == userId)
            .OrderByDescending(d => d.CreatedAtUtc).Take(ListSize).ToListAsync(ct);
        var dtos = await ToDtosAsync(duels, userId, ct);
        // what needs the player first: answer an invitation, then play, then history
        var ordered = dtos.OrderBy(d => d.NeedsMyResponse ? 0 : d.CanPlay ? 1 : d.Status is "Pending" or "Active" ? 2 : 3)
            .ThenByDescending(d => d.CreatedAtUtc).ToList();
        return new DuelListDto(ordered, await OpenCountAsync(userId, ct), Balance.MaxOpenDuels, Balance.WinXp, Balance.DrawXp, Balance.LoseXp);
    }

    public async Task<DuelDto> GetAsync(Guid userId, Guid duelId, CancellationToken ct)
    {
        await SettleOverdueAsync(userId, ct);
        return await LoadDtoAsync(userId, duelId, ct);
    }

    private async Task<DuelDto> LoadDtoAsync(Guid userId, Guid duelId, CancellationToken ct)
    {
        var duel = await db.Duels.AsNoTracking().Include(d => d.Challenger).Include(d => d.Opponent)
            .FirstOrDefaultAsync(d => d.Id == duelId && (d.ChallengerId == userId || d.OpponentId == userId), ct)
            ?? throw new NotFoundException("Duel not found.");   // not a participant: indistinguishable from a missing duel
        return (await ToDtosAsync([duel], userId, ct))[0];
    }

    private Task<int> OpenCountAsync(Guid userId, CancellationToken ct)
        => db.Duels.AsNoTracking().CountAsync(d => (d.ChallengerId == userId || d.OpponentId == userId)
                                                   && (d.Status == DuelStatus.Pending || d.Status == DuelStatus.Active), ct);

    private async Task<List<DuelDto>> ToDtosAsync(List<Duel> duels, Guid me, CancellationToken ct)
    {
        var ids = duels.Select(d => d.Id).ToList();
        var attempts = await db.DuelAttempts.AsNoTracking().Where(a => ids.Contains(a.DuelId)).OrderBy(a => a.CreatedAtUtc).ToListAsync(ct);
        var now = Now;
        var result = new List<DuelDto>();
        foreach (var d in duels)
        {
            var iAmChallenger = d.ChallengerId == me;
            var other = iAmChallenger ? d.Opponent! : d.Challenger!;
            var mine = attempts.Where(a => a.DuelId == d.Id && a.UserId == me).ToList();
            var theirs = attempts.Where(a => a.DuelId == d.Id && a.UserId != me).ToList();
            var over = d.Status is DuelStatus.Completed or DuelStatus.Expired && d.AcceptedAtUtc is not null;   // an unanswered invitation has no result
            var myDone = iAmChallenger ? d.ChallengerDone : d.OpponentDone;
            var otherDone = iAmChallenger ? d.OpponentDone : d.ChallengerDone;
            var myBest = mine.Count > 0 ? mine.Max(a => a.Score) : (int?)null;
            var left = Math.Max(0, Balance.AttemptsPerPlayer - mine.Count);

            // the invited player only learns the colour once they accept
            var colourVisible = iAmChallenger || d.AcceptedAtUtc is not null;
            var color = colourVisible ? AtlasCatalog.FindByKey(d.ColorKey) : null;

            string? outcome = null;
            if (over)
                outcome = d.ChallengerBest is null && d.OpponentBest is null ? "none"
                        : d.WinnerId is null ? "draw" : d.WinnerId == me ? "win" : "lose";

            var active = d.Status == DuelStatus.Active;
            result.Add(new DuelDto(
                d.Id, d.Status.ToString(), iAmChallenger ? "challenger" : "opponent", other.Id, other.UserName, MediaPaths.ToUrl(other.AvatarKey),
                color?.Hex, color?.Name, d.CreatedAtUtc, d.ExpiresAtUtc, Balance.AttemptsPerPlayer,
                mine.Select(a => a.Score).ToList(), left, myBest, myDone, otherDone, theirs.Count,
                over ? (iAmChallenger ? d.OpponentBest : d.ChallengerBest) : null,
                outcome, iAmChallenger ? d.ChallengerXp : d.OpponentXp,
                active && !myDone && left > 0 && now < d.ExpiresAtUtc,
                d.Status == DuelStatus.Pending && !iAmChallenger,
                Balance.WinXp, Balance.DrawXp, Balance.LoseXp));
        }
        return result;
    }

    // ------------------------------------------------------------------ invitation
    public async Task<DuelDto> CreateAsync(Guid userId, Guid opponentId, CancellationToken ct)
    {
        if (opponentId == userId) throw new ValidationAppException("opponentId", "You cannot challenge yourself.");
        if (!await db.Profiles.AnyAsync(p => p.Id == opponentId, ct)) throw new NotFoundException("User not found.");

        var key = Friendship.MakePairKey(userId, opponentId);
        var friends = await db.Friendships.AsNoTracking().AnyAsync(f => f.PairKey == key && f.Status == FriendshipStatus.Accepted, ct);
        if (!friends) throw new ForbiddenException("You can only challenge your friends.");

        await SettleOverdueAsync(userId, ct);
        await SettleOverdueAsync(opponentId, ct);

        var pairOpen = await db.Duels.AsNoTracking().AnyAsync(d =>
            ((d.ChallengerId == userId && d.OpponentId == opponentId) || (d.ChallengerId == opponentId && d.OpponentId == userId))
            && (d.Status == DuelStatus.Pending || d.Status == DuelStatus.Active), ct);
        if (pairOpen) throw new ConflictException("There is already an open duel with this friend.");
        if (await OpenCountAsync(userId, ct) >= Balance.MaxOpenDuels)
            throw new TooManyRequestsException($"You can have at most {Balance.MaxOpenDuels} open duels at a time.");
        if (await OpenCountAsync(opponentId, ct) >= Balance.MaxOpenDuels)
            throw new ConflictException("This friend has too many open duels right now.");

        var color = AtlasCatalog.All[Random.Shared.Next(AtlasCatalog.All.Count)];
        var now = Now;
        var duel = new Duel
        {
            Id = Guid.NewGuid(), ChallengerId = userId, OpponentId = opponentId, ColorKey = color.Key, Status = DuelStatus.Pending,
            CreatedAtUtc = now, UpdatedAtUtc = now, ExpiresAtUtc = now.AddHours(Balance.InviteHours),
        };
        db.Duels.Add(duel);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return await LoadDtoAsync(userId, duel.Id, ct);
    }

    public async Task<DuelDto> AcceptAsync(Guid userId, Guid duelId, CancellationToken ct)
    {
        await SettleOverdueAsync(userId, ct);
        await MutateAsync(userId, duelId, d =>
        {
            if (d.OpponentId != userId) throw new ForbiddenException("Only the invited player can accept.");
            if (d.Status != DuelStatus.Pending) throw new ConflictException("This duel can no longer be accepted.");
            var now = Now;
            d.Status = DuelStatus.Active; d.AcceptedAtUtc = now; d.UpdatedAtUtc = now; d.ExpiresAtUtc = now.AddHours(Balance.PlayHours);
        }, ct);
        return await LoadDtoAsync(userId, duelId, ct);
    }

    public async Task<DuelDto> DeclineAsync(Guid userId, Guid duelId, CancellationToken ct)
    {
        await SettleOverdueAsync(userId, ct);
        await MutateAsync(userId, duelId, d =>
        {
            if (d.Status != DuelStatus.Pending) throw new ConflictException("Only a pending invitation can be declined or withdrawn.");
            d.Status = DuelStatus.Declined; d.FinishedAtUtc = Now; d.UpdatedAtUtc = Now;
        }, ct);
        return await LoadDtoAsync(userId, duelId, ct);
    }

    /// <summary>Loads the duel tracked, lets <paramref name="change"/> modify it and saves; a concurrent change makes it re-check against the fresh state.</summary>
    private async Task MutateAsync(Guid userId, Guid duelId, Action<Duel> change, CancellationToken ct)
    {
        for (var i = 0; i < Retries; i++)
        {
            var duel = await db.Duels.FirstOrDefaultAsync(d => d.Id == duelId && (d.ChallengerId == userId || d.OpponentId == userId), ct)
                       ?? throw new NotFoundException("Duel not found.");
            change(duel);
            try { await db.SaveChangesAsync(ct); db.ChangeTracker.Clear(); return; }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
        }
        throw new ConflictException("The duel changed at the same time. Please try again.");
    }

    // ------------------------------------------------------------------ playing
    public async Task<DuelAttemptResultDto> SubmitAttemptAsync(DuelSubmission s, CancellationToken ct)
    {
        await SettleOverdueAsync(s.UserId, ct);

        var duel = await db.Duels.AsNoTracking().FirstOrDefaultAsync(d => d.Id == s.DuelId && (d.ChallengerId == s.UserId || d.OpponentId == s.UserId), ct)
                   ?? throw new NotFoundException("Duel not found.");
        EnsurePlayable(duel, s.UserId);
        var color = AtlasCatalog.FindByKey(duel.ColorKey)!;

        var used = await db.DuelAttempts.AsNoTracking().CountAsync(a => a.DuelId == duel.Id && a.UserId == s.UserId, ct);
        if (used >= Balance.AttemptsPerPlayer) throw new TooManyRequestsException($"You have used all {Balance.AttemptsPerPlayer} attempts of this duel.");

        // official score: computed here from the photo, never taken from the client
        var outcome = colorMatch.Evaluate(s.Photo, color.Hex);
        var hash = Convert.ToHexString(SHA256.HashData(outcome.Image.JpegBytes));

        // basic copy check: the same photo cannot be played twice in one duel, by either player (see the security notes for its limits)
        if (await db.DuelAttempts.AsNoTracking().AnyAsync(a => a.DuelId == duel.Id && a.PhotoHash == hash, ct))
            throw new ConflictException("This photo was already used in this duel. Take a new photo.");

        for (var i = 0; i < Retries; i++)
        {
            try { return await CommitAttemptAsync(s, outcome, hash, ct); }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex)) { db.ChangeTracker.Clear(); }
        }
        throw new ConflictException("The duel changed at the same time. Please try again.");
    }

    private void EnsurePlayable(Duel d, Guid userId)
    {
        if (d.Status == DuelStatus.Pending) throw new ConflictException("The duel has not been accepted yet.");
        if (d.Status != DuelStatus.Active || Now >= d.ExpiresAtUtc) throw new ConflictException("This duel is over.");
        if (d.ChallengerId == userId ? d.ChallengerDone : d.OpponentDone) throw new ConflictException("You have already finished this duel.");
    }

    private async Task<DuelAttemptResultDto> CommitAttemptAsync(DuelSubmission s, ColorMatchOutcome outcome, string hash, CancellationToken ct)
    {
        var duel = await db.Duels.FirstOrDefaultAsync(d => d.Id == s.DuelId && (d.ChallengerId == s.UserId || d.OpponentId == s.UserId), ct)
                   ?? throw new NotFoundException("Duel not found.");
        EnsurePlayable(duel, s.UserId);
        var iAmChallenger = duel.ChallengerId == s.UserId;
        var now = Now;

        var mine = await db.DuelAttempts.AsNoTracking().Where(a => a.DuelId == duel.Id && a.UserId == s.UserId).Select(a => a.Score).ToListAsync(ct);
        if (mine.Count >= Balance.AttemptsPerPlayer) throw new TooManyRequestsException($"You have used all {Balance.AttemptsPerPlayer} attempts of this duel.");

        var attemptId = Guid.NewGuid();
        db.DuelAttempts.Add(new DuelAttempt
        {
            Id = attemptId, DuelId = duel.Id, UserId = s.UserId, Score = outcome.Score, DeltaE = Math.Round(outcome.DeltaE, 3),
            SampledColorHex = outcome.Found.ToHex(), PhotoHash = hash, CreatedAtUtc = now,
        });
        duel.UpdatedAtUtc = now;   // makes the row version reject a parallel attempt of the same duel

        var previousBest = mine.Count > 0 ? mine.Max() : 0;
        var finished = mine.Count + 1 >= Balance.AttemptsPerPlayer;
        if (finished) { if (iAmChallenger) duel.ChallengerDone = true; else duel.OpponentDone = true; }

        var settled = await ResolveIfBothDoneAsync(duel, extra: (s.UserId, outcome.Score), ct);
        await db.SaveChangesAsync(ct);

        var dto = await RewardOutcomeAsync(settled, s.UserId, ct);
        db.ChangeTracker.Clear();
        var view = await LoadDtoAsync(s.UserId, duel.Id, ct);
        var best = Math.Max(previousBest, outcome.Score);
        return new DuelAttemptResultDto(attemptId, AtlasCatalog.FindByKey(duel.ColorKey)!.Hex, outcome.Found.ToHex(), Math.Round(outcome.DeltaE, 2), outcome.Score,
            ScoreTiers.For(outcome.Score).ToString(), outcome.Score > previousBest, best, Math.Max(0, Balance.AttemptsPerPlayer - mine.Count - 1),
            finished, settled is not null, dto.xp, dto.total, dto.level, dto.leveledUp, view, outcome.RegionDto);
    }

    public async Task<DuelDto> FinishAsync(Guid userId, Guid duelId, CancellationToken ct)
    {
        await SettleOverdueAsync(userId, ct);
        for (var i = 0; i < Retries; i++)
        {
            var duel = await db.Duels.FirstOrDefaultAsync(d => d.Id == duelId && (d.ChallengerId == userId || d.OpponentId == userId), ct)
                       ?? throw new NotFoundException("Duel not found.");
            EnsurePlayable(duel, userId);
            if (duel.ChallengerId == userId) duel.ChallengerDone = true; else duel.OpponentDone = true;
            duel.UpdatedAtUtc = Now;
            await ResolveIfBothDoneAsync(duel, extra: null, ct);
            try { await db.SaveChangesAsync(ct); db.ChangeTracker.Clear(); return await LoadDtoAsync(userId, duelId, ct); }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex)) { db.ChangeTracker.Clear(); }
        }
        throw new ConflictException("The duel changed at the same time. Please try again.");
    }

    // ------------------------------------------------------------------ settling
    private sealed record Settlement(Dictionary<Guid, RewardResult> Rewards);

    /// <summary>Both players done: settle the duel now (stages status and XP; the caller saves).</summary>
    private async Task<Settlement?> ResolveIfBothDoneAsync(Duel d, (Guid UserId, int Score)? extra, CancellationToken ct)
        => d.ChallengerDone && d.OpponentDone ? await ResolveAsync(d, byExpiry: false, extra, ct) : null;

    /// <summary>
    /// Decides winner and XP from the stored attempts (plus the attempt that is being added right now). Winner = higher best score;
    /// a player who never played loses to one who did; equal bests (or nobody played) have no winner.
    /// </summary>
    private async Task<Settlement> ResolveAsync(Duel d, bool byExpiry, (Guid UserId, int Score)? extra, CancellationToken ct)
    {
        var rows = await db.DuelAttempts.AsNoTracking().Where(a => a.DuelId == d.Id).Select(a => new { a.UserId, a.Score }).ToListAsync(ct);
        var scores = rows.Select(r => (r.UserId, r.Score)).ToList();
        if (extra is { } e) scores.Add(e);

        int? Best(Guid u) { var l = scores.Where(x => x.UserId == u).Select(x => x.Score).ToList(); return l.Count > 0 ? l.Max() : null; }
        var cBest = Best(d.ChallengerId); var oBest = Best(d.OpponentId);
        var played = (c: scores.Any(x => x.UserId == d.ChallengerId), o: scores.Any(x => x.UserId == d.OpponentId));

        Guid? winner = (cBest, oBest) switch
        {
            (null, null) => null,
            (not null, null) => d.ChallengerId,
            (null, not null) => d.OpponentId,
            var (c, o) when c > o => d.ChallengerId,
            var (c, o) when o > c => d.OpponentId,
            _ => null,
        };

        d.ChallengerBest = cBest; d.OpponentBest = oBest; d.WinnerId = winner;
        d.Status = byExpiry ? DuelStatus.Expired : DuelStatus.Completed;
        d.FinishedAtUtc = Now; d.UpdatedAtUtc = Now;
        d.ChallengerDone = true; d.OpponentDone = true;

        var granted = new Dictionary<Guid, RewardResult>();
        if (cBest is null && oBest is null) return new Settlement(granted);   // nobody played: no result, no XP

        d.ChallengerXp = await PayAsync(d, d.ChallengerId, played.c, winner, granted, ct);
        d.OpponentXp = await PayAsync(d, d.OpponentId, played.o, winner, granted, ct);
        return new Settlement(granted);
    }

    private async Task<int> PayAsync(Duel d, Guid user, bool played, Guid? winner, Dictionary<Guid, RewardResult> granted, CancellationToken ct)
    {
        if (!played) return 0;   // forfeited: nothing
        var amount = winner is null ? Balance.DrawXp : winner == user ? Balance.WinXp : Balance.LoseXp;

        if (Balance.DailyXpCap > 0)
        {
            var dayStart = Now.Date;
            var today = await db.XpTransactions.AsNoTracking()
                .Where(t => t.UserId == user && t.SourceType == XpSourceType.DuelResult && t.CreatedAtUtc >= dayStart).SumAsync(t => (int?)t.Amount, ct) ?? 0;
            amount = Math.Min(amount, Math.Max(0, Balance.DailyXpCap - today));
        }
        if (amount <= 0) return 0;

        var r = await rewards.GrantAsync(new RewardRequest(user, amount, XpSourceType.DuelResult, d.Id.ToString("N"),
            winner is null ? "Friend duel: draw" : winner == user ? "Friend duel: won" : "Friend duel: played", RewardKey(d.Id, user)), ct);
        if (!r.Granted) return 0;
        granted[user] = r;
        return amount;
    }

    private async Task<(int xp, int total, int level, bool leveledUp)> RewardOutcomeAsync(Settlement? settled, Guid userId, CancellationToken ct)
    {
        var p = await db.Progress.AsNoTracking().FirstAsync(x => x.UserId == userId, ct);
        if (settled is not null && settled.Rewards.TryGetValue(userId, out var r)) return (r.Amount, p.TotalXp, p.Level, r.LeveledUp);
        return (0, p.TotalXp, p.Level, false);
    }

    /// <summary>
    /// Lazy expiry: settles every overdue duel of the player, one unit of work each. Pending invitations simply expire; an active duel is
    /// resolved from the attempts made so far. Safe to call from any request, in parallel: the row version lets exactly one settle win.
    /// </summary>
    private async Task SettleOverdueAsync(Guid userId, CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            var now = Now;
            var duel = await db.Duels.FirstOrDefaultAsync(d => (d.ChallengerId == userId || d.OpponentId == userId)
                && (d.Status == DuelStatus.Pending || d.Status == DuelStatus.Active) && d.ExpiresAtUtc <= now, ct);
            if (duel is null) { db.ChangeTracker.Clear(); return; }

            try
            {
                if (duel.Status == DuelStatus.Pending) { duel.Status = DuelStatus.Expired; duel.FinishedAtUtc = now; duel.UpdatedAtUtc = now; }
                else await ResolveAsync(duel, byExpiry: true, extra: null, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException) { /* someone settled it first */ }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex)) { /* the reward was booked by the winner of the race */ }
            db.ChangeTracker.Clear();
        }
    }

    private static bool IsDuplicateKey(DbUpdateException ex)
        => ex.InnerException?.Message.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase) == true;
}
