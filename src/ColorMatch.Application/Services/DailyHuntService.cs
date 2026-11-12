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
/// Daily Hunt: three server-chosen colours per game day (easy / medium / hard). A hunt is completed by the first attempt that reaches
/// the minimum score; that attempt pays the hunt's XP once (idempotency key per hunt and player), later attempts can only improve the
/// best score. Completing all three pays a one-time bonus. A completed hunt keeps the streak alive.
/// </summary>
public sealed class DailyHuntService(
    IAppDbContext db,
    IColorMatchService colorMatch,
    IRewardService rewards,
    IProgressionService progression,
    IAtlasService atlas,
    IChallengeService challenges,
    IFileStorage storage,
    LevelCurve curve,
    IOptions<GameBalance> balanceOptions,
    TimeProvider clock) : IDailyHuntService
{
    private static readonly HuntDifficulty[] Order = [HuntDifficulty.Easy, HuntDifficulty.Medium, HuntDifficulty.Hard];
    private const int CommitAttempts = 5;

    private DailyHuntBalance Balance => balanceOptions.Value.DailyHunt;

    public static string HuntKey(int huntId, Guid userId) => $"daily-hunt:{huntId}:{userId:N}";
    public static string BonusKey(DateOnly date, Guid userId) => $"daily-hunt-bonus:{date:yyyy-MM-dd}:{userId:N}";

    // ------------------------------------------------------------------ today's hunts
    /// <summary>Returns the three hunts of <paramref name="date"/>, creating the missing ones. Safe under parallel requests (unique index).</summary>
    private async Task<List<DailyHunt>> EnsureHuntsAsync(DateOnly date, CancellationToken ct)
    {
        var rows = await db.DailyHunts.AsNoTracking().Where(h => h.ChallengeDate == date).ToListAsync(ct);
        if (rows.Count < Order.Length)
        {
            foreach (var d in Order.Where(d => rows.All(r => r.Difficulty != d)))
            {
                var pick = DailyHuntPalette.ForDate(date, d);
                db.DailyHunts.Add(new DailyHunt
                {
                    ChallengeDate = date, Difficulty = d, Name = pick.Name, TargetColorHex = pick.Hex,
                    BaseXp = Balance.XpFor(d), MinScore = Balance.MinScore,
                });
            }
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { /* another request created them first: use theirs */ }
            db.ChangeTracker.Clear();
            rows = await db.DailyHunts.AsNoTracking().Where(h => h.ChallengeDate == date).ToListAsync(ct);
        }
        return rows.OrderBy(r => r.Difficulty).ToList();
    }

    public async Task<DailyHuntsDto> GetTodayAsync(Guid userId, CancellationToken ct)
    {
        var date = challenges.TodayLocal();
        var hunts = await EnsureHuntsAsync(date, ct);
        var ids = hunts.Select(h => h.Id).ToList();

        // at most MaxAttemptsPerHunt × 3 rows per player and day
        var mine = await db.DailyHuntAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && ids.Contains(a.DailyHuntId))
            .Select(a => new { a.DailyHuntId, a.Score, a.Passed, a.XpAwarded, a.PhotoKey, a.CreatedAtUtc })
            .ToListAsync(ct);

        var dtos = hunts.Select(h =>
        {
            var rows = mine.Where(a => a.DailyHuntId == h.Id).ToList();
            var best = rows.OrderByDescending(a => a.Score).ThenBy(a => a.CreatedAtUtc).FirstOrDefault();
            var attempts = rows.Count;
            return new DailyHuntDto(h.Id, h.ChallengeDate, h.Difficulty.ToString(), h.Name, h.TargetColorHex, h.BaseXp, h.MinScore,
                best?.Score ?? 0, attempts > 0 ? ScoreTiers.For(best!.Score).ToString() : null, rows.Any(a => a.Passed), attempts,
                rows.Sum(a => a.XpAwarded), best is null ? null : MediaPaths.ToUrl(best.PhotoKey));
        }).ToList();

        var bonusEarned = await db.XpTransactions.AsNoTracking().AnyAsync(t => t.IdempotencyKey == BonusKey(date, userId), ct);
        return new DailyHuntsDto(date, challenges.NextResetUtc(date), dtos, BuildProgress(dtos, bonusEarned));
    }

    public async Task<DailyProgressDto> GetProgressAsync(Guid userId, CancellationToken ct)
        => (await GetTodayAsync(userId, ct)).Progress;

    public async Task<WeekDto> GetWeekAsync(Guid userId, CancellationToken ct)
    {
        var today = challenges.TodayLocal();
        var first = today.AddDays(-6);
        // distinct hunts with at least one valid attempt, per game day
        var done = await (from a in db.DailyHuntAttempts.AsNoTracking()
                          join h in db.DailyHunts.AsNoTracking() on a.DailyHuntId equals h.Id
                          where a.UserId == userId && a.Passed && h.ChallengeDate >= first && h.ChallengeDate <= today
                          select new { h.ChallengeDate, h.Id }).Distinct().ToListAsync(ct);
        var perDay = done.GroupBy(x => x.ChallengeDate).ToDictionary(g => g.Key, g => g.Count());
        const int total = 3;
        var days = Enumerable.Range(0, 7).Select(i => first.AddDays(i))
            .Select(d => new WeekDayDto(d, Math.Min(total, perDay.GetValueOrDefault(d)), total, d == today)).ToList();
        return new WeekDto(days, days.Count(d => d.Completed > 0), days.Count(d => d.Completed >= d.Total));
    }

    private DailyProgressDto BuildProgress(IReadOnlyList<DailyHuntDto> hunts, bool bonusEarned)
    {
        var completed = hunts.Count(h => h.Completed);
        var bonus = Balance.CompletionBonusXp;
        return new DailyProgressDto(completed, hunts.Count, hunts.Count > 0 && completed == hunts.Count, bonus, bonusEarned,
            hunts.Sum(h => h.XpEarned) + (bonusEarned ? bonus : 0), hunts.Sum(h => h.BaseXp) + bonus);
    }

    // ------------------------------------------------------------------ an attempt
    public async Task<DailyHuntAttemptResultDto> SubmitAttemptAsync(DailyHuntSubmission s, CancellationToken ct)
    {
        var hunt = await db.DailyHunts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == s.HuntId, ct)
                   ?? throw new NotFoundException("Hunt not found.");
        var today = challenges.TodayLocal();
        if (hunt.ChallengeDate != today) throw new ConflictException("This hunt is no longer active.");

        var used = await db.DailyHuntAttempts.AsNoTracking().CountAsync(a => a.UserId == s.UserId && a.DailyHuntId == hunt.Id, ct);
        if (used >= Balance.MaxAttemptsPerHunt)
            throw new TooManyRequestsException($"You have used all {Balance.MaxAttemptsPerHunt} attempts of this hunt for today.");

        // official score: computed here from the photo, never taken from the client
        var outcome = colorMatch.Evaluate(s.Photo, hunt.TargetColorHex);
        var hash = Convert.ToHexString(SHA256.HashData(outcome.Image.JpegBytes));

        // basic re-use check: one photo cannot complete two different hunts (see the security notes for its limits)
        var reused = await db.DailyHuntAttempts.AsNoTracking()
            .AnyAsync(a => a.UserId == s.UserId && a.PhotoHash == hash && a.DailyHuntId != hunt.Id && a.Passed, ct);
        if (reused) throw new ConflictException("This photo was already used to complete another hunt. Take a new photo.");

        var attemptId = Guid.NewGuid();
        var photoKey = $"{MediaPaths.UserPrefix(s.UserId)}hunt/{attemptId:N}.jpg";
        await using (var ms = new MemoryStream(outcome.Image.JpegBytes))
            await storage.SaveAsync(photoKey, ms, ct);

        var saved = false;
        try
        {
            for (var attempt = 1; attempt <= CommitAttempts; attempt++)
            {
                try
                {
                    var result = await CommitAsync(s, hunt, today, outcome, hash, attemptId, photoKey, ct);
                    saved = true;
                    return result;
                }
                catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
                catch (DbUpdateException ex) when (IsDuplicateOfOurs(ex))
                {
                    db.ChangeTracker.Clear();   // an identical reward was booked at the same moment: re-evaluate against it
                }
            }
            throw new ConflictException("Your progress changed at the same time. Please try again.");
        }
        finally
        {
            if (!saved) await storage.DeleteAsync(photoKey, CancellationToken.None);
        }
    }

    private static bool IsDuplicateOfOurs(DbUpdateException ex)
        => ex.InnerException?.Message is { } m
           && (m.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase) || m.Contains("UserAtlasColors", StringComparison.OrdinalIgnoreCase));

    /// <summary>The whole attempt as one unit of work: attempt row, XP, streak and bonus commit together or not at all.</summary>
    private async Task<DailyHuntAttemptResultDto> CommitAsync(
        DailyHuntSubmission s, DailyHunt hunt, DateOnly today, ColorMatchOutcome outcome, string hash, Guid attemptId, string photoKey, CancellationToken ct)
    {
        var passed = outcome.Score >= hunt.MinScore;

        var previousBest = await db.DailyHuntAttempts.AsNoTracking()
            .Where(a => a.UserId == s.UserId && a.DailyHuntId == hunt.Id).MaxAsync(a => (int?)a.Score, ct) ?? 0;
        var alreadyCompleted = await db.DailyHuntAttempts.AsNoTracking()
            .AnyAsync(a => a.UserId == s.UserId && a.DailyHuntId == hunt.Id && a.Passed, ct);
        var firstCompletion = passed && !alreadyCompleted;

        var attemptRow = new DailyHuntAttempt
        {
            Id = attemptId, UserId = s.UserId, DailyHuntId = hunt.Id, Score = outcome.Score, DeltaE = Math.Round(outcome.DeltaE, 3),
            SampledColorHex = outcome.Found.ToHex(), Passed = passed, XpAwarded = 0,
            RegionX = outcome.Region.X, RegionY = outcome.Region.Y, RegionRadius = outcome.Region.Radius,
            PhotoKey = photoKey, PhotoHash = hash, CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.DailyHuntAttempts.Add(attemptRow);

        RewardResult? huntReward = null, bonusReward = null;
        if (firstCompletion)
        {
            huntReward = await rewards.GrantAsync(new RewardRequest(s.UserId, hunt.BaseXp, XpSourceType.DailyHunt, hunt.Id.ToString(),
                $"Daily hunt {today:yyyy-MM-dd} ({hunt.Difficulty})", HuntKey(hunt.Id, s.UserId)), ct);
            if (huntReward.Granted) attemptRow.XpAwarded = hunt.BaseXp;
        }

        StreakUpdate? streak = null;
        if (passed) streak = await progression.RecordQualifyingActivityAsync(s.UserId, ct);

        // how many of today's hunts are done, counting this attempt
        var todayIds = await db.DailyHunts.AsNoTracking().Where(h => h.ChallengeDate == today).Select(h => h.Id).ToListAsync(ct);
        var done = await db.DailyHuntAttempts.AsNoTracking()
            .Where(a => a.UserId == s.UserId && a.Passed && todayIds.Contains(a.DailyHuntId)).Select(a => a.DailyHuntId).Distinct().ToListAsync(ct);
        if (passed && !done.Contains(hunt.Id)) done.Add(hunt.Id);

        if (firstCompletion && done.Count >= todayIds.Count && todayIds.Count == Order.Length && Balance.CompletionBonusXp > 0)
        {
            bonusReward = await rewards.GrantAsync(new RewardRequest(s.UserId, Balance.CompletionBonusXp, XpSourceType.DailyHuntCompletion,
                today.ToString("yyyy-MM-dd"), $"All three daily hunts completed ({today:yyyy-MM-dd})", BonusKey(today, s.UserId)), ct);
        }

        // a valid attempt on an atlas colour unlocks it (once), and may finish a collection
        AtlasUnlockStaged? atlasStaged = passed
            ? await atlas.StageUnlockAsync(s.UserId, hunt.TargetColorHex, attemptId, outcome.Score, ct)
            : null;

        await db.SaveChangesAsync(ct);

        var progress = await db.Progress.AsNoTracking().FirstAsync(p => p.UserId == s.UserId, ct);
        var levelBefore = huntReward?.PreviousLevel ?? bonusReward?.PreviousLevel ?? atlasStaged?.CompletionReward?.PreviousLevel ?? progress.Level;
        var title = curve.TitleForLevel(progress.Level);

        return new DailyHuntAttemptResultDto(
            attemptId, hunt.Id, hunt.Difficulty.ToString(), hunt.TargetColorHex, outcome.Found.ToHex(), Math.Round(outcome.DeltaE, 2),
            outcome.Score, ScoreTiers.For(outcome.Score).ToString(), passed, hunt.MinScore,
            huntReward is { Granted: true } ? hunt.BaseXp : 0, huntReward is { Granted: true },
            Math.Max(previousBest, outcome.Score), outcome.Score > previousBest,
            bonusReward is { Granted: true } ? Balance.CompletionBonusXp : 0,
            done.Count, todayIds.Count > 0 && done.Count >= todayIds.Count,
            progress.TotalXp, progress.Level, progress.Level > levelBefore, title, title != curve.TitleForLevel(levelBefore),
            StreakRules.Effective(new StreakState(progress.CurrentStreak, progress.LongestStreak, progress.LastQualifyingActivityDate), today),
            streak?.Extended ?? false, photoKey, MediaPaths.ToUrl(photoKey)!, atlasStaged?.Unlock, outcome.RegionDto);
    }

    // ------------------------------------------------------------------ ranking
    public async Task<LeaderboardDto> GetLeaderboardAsync(Guid userId, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 50);
        var hunts = await EnsureHuntsAsync(challenges.TodayLocal(), ct);
        var ids = hunts.Select(h => h.Id).ToList();

        var bestPerHunt = db.DailyHuntAttempts.AsNoTracking()
            .Where(a => ids.Contains(a.DailyHuntId))
            .GroupBy(a => new { a.UserId, a.DailyHuntId })
            .Select(g => new { g.Key.UserId, Best = g.Max(a => a.Score) });
        var totals = bestPerHunt.GroupBy(x => x.UserId).Select(g => new { UserId = g.Key, Total = g.Sum(x => x.Best) });

        var players = await totals.CountAsync(ct);
        var top = await totals.OrderByDescending(x => x.Total).ThenBy(x => x.UserId).Take(take)
            .Join(db.Profiles.AsNoTracking(), x => x.UserId, p => p.Id, (x, p) => new { x.UserId, p.UserName, p.AvatarKey, x.Total })
            .OrderByDescending(x => x.Total).ThenBy(x => x.UserId)
            .ToListAsync(ct);

        var entries = top.Select((x, i) => new LeaderboardEntryDto(i + 1, x.UserId, x.UserName, MediaPaths.ToUrl(x.AvatarKey), x.Total, x.UserId == userId)).ToList();
        int? myRank = entries.FirstOrDefault(e => e.IsMe)?.Rank;
        if (myRank is null)
        {
            var mine = await totals.Where(x => x.UserId == userId).Select(x => (int?)x.Total).FirstOrDefaultAsync(ct);
            if (mine is { } m) myRank = 1 + await totals.CountAsync(x => x.Total > m, ct);
        }
        return new LeaderboardDto(entries, myRank, players);
    }
}
