using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using ColorMatch.Domain.Entities;
using ColorMatch.Domain.Enums;
using ColorMatch.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace ColorMatch.Application.Services;

public sealed record RewardRequest(Guid UserId, int Amount, XpSourceType Source, string? SourceId, string Reason, string IdempotencyKey);

/// <summary>Outcome of a reward. <see cref="Granted"/> is false when the idempotency key had already been booked (nothing changed).</summary>
public sealed record RewardResult(
    bool Granted, int Amount, int TotalXp, int PreviousLevel, int Level, string Title,
    bool LeveledUp, bool TitleChanged);

public sealed record StreakUpdate(int Current, int Longest, bool Extended);

/// <summary>The single place where XP is created. Every game mode pays out through here.</summary>
public interface IRewardService
{
    /// <summary>
    /// Books the XP in the current unit of work: adds the ledger row and raises the player's total and level, but does NOT save.
    /// The caller saves together with its own changes (attempt, board …) so everything commits or fails as one.
    /// </summary>
    Task<RewardResult> GrantAsync(RewardRequest request, CancellationToken ct = default);

    /// <summary>Standalone variant: grants and saves, retrying on concurrent XP changes. Clears the change tracker on conflict, so only use it when nothing else is pending.</summary>
    Task<RewardResult> GrantAndSaveAsync(RewardRequest request, CancellationToken ct = default);
}

public interface IProgressionService
{
    Task<ProgressionDto> GetAsync(Guid userId, CancellationToken ct);
    ProgressionDto Describe(UserProgress progress, DateOnly today);
    /// <summary>Counts one qualifying activity for today's game day (stages the change, caller saves). A day counts once.</summary>
    Task<StreakUpdate> RecordQualifyingActivityAsync(Guid userId, CancellationToken ct = default);
    Task<PagedResult<XpTransactionDto>> GetHistoryAsync(Guid userId, string? cursor, int pageSize, CancellationToken ct);
}

public sealed class RewardService(IAppDbContext db, LevelCurve curve, TimeProvider clock) : IRewardService
{
    private const int MaxKeyLength = 128;
    private const int Attempts = 5;

    public async Task<RewardResult> GrantAsync(RewardRequest r, CancellationToken ct = default)
    {
        Validate(r);

        var progress = await db.Progress.FirstOrDefaultAsync(p => p.UserId == r.UserId, ct)
                       ?? throw new NotFoundException("User progress not found.");

        var already = db.XpTransactions.Local.Any(t => t.IdempotencyKey == r.IdempotencyKey)
                      || await db.XpTransactions.AsNoTracking().AnyAsync(t => t.IdempotencyKey == r.IdempotencyKey, ct);
        if (already)
            return new RewardResult(false, 0, progress.TotalXp, progress.Level, progress.Level, curve.TitleForLevel(progress.Level), false, false);

        var previousLevel = progress.Level;
        var previousTitle = curve.TitleForLevel(previousLevel);

        progress.TotalXp = (int)Math.Min(int.MaxValue, (long)progress.TotalXp + r.Amount);
        progress.Level = curve.LevelForXp(progress.TotalXp);

        db.XpTransactions.Add(new XpTransaction
        {
            Id = Guid.NewGuid(),
            UserId = r.UserId,
            Amount = r.Amount,
            SourceType = r.Source,
            SourceId = r.SourceId,
            Reason = r.Reason,
            IdempotencyKey = r.IdempotencyKey,
            BalanceAfter = progress.TotalXp,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        });

        var title = curve.TitleForLevel(progress.Level);
        return new RewardResult(true, r.Amount, progress.TotalXp, previousLevel, progress.Level, title,
            progress.Level > previousLevel, title != previousTitle);
    }

    public async Task<RewardResult> GrantAndSaveAsync(RewardRequest request, CancellationToken ct = default)
    {
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                var result = await GrantAsync(request, ct);
                if (result.Granted) await db.SaveChangesAsync(ct);
                return result;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();   // someone else changed the XP at the same time: reload and try again
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex))
            {
                db.ChangeTracker.Clear();   // lost a race against an identical reward: it was booked exactly once
                var current = await db.Progress.AsNoTracking().FirstAsync(p => p.UserId == request.UserId, ct);
                return new RewardResult(false, 0, current.TotalXp, current.Level, current.Level, curve.TitleForLevel(current.Level), false, false);
            }
        }
        throw new ConflictException("Your progress changed at the same time. Please try again.");
    }

    private static bool IsDuplicateKey(DbUpdateException ex)
        => ex.InnerException?.Message.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase) == true;

    private static void Validate(RewardRequest r)
    {
        if (r.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(r), "Reward amount must be positive.");
        if (string.IsNullOrWhiteSpace(r.IdempotencyKey) || r.IdempotencyKey.Length > MaxKeyLength)
            throw new ArgumentException("A reward needs an idempotency key of at most 128 characters.", nameof(r));
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 200)
            throw new ArgumentException("A reward needs a reason of at most 200 characters.", nameof(r));
    }
}

public sealed class ProgressionService(IAppDbContext db, IChallengeService challenges, LevelCurve curve) : IProgressionService
{
    public async Task<ProgressionDto> GetAsync(Guid userId, CancellationToken ct)
    {
        var progress = await db.Progress.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct)
                       ?? throw new NotFoundException("User not found.");
        return Describe(progress, challenges.TodayLocal());
    }

    public ProgressionDto Describe(UserProgress p, DateOnly today)
    {
        var level = p.Level;
        var start = curve.XpForLevel(level);
        var next = curve.XpForLevel(level + 1);
        var into = Math.Max(0, p.TotalXp - start);
        var span = Math.Max(1, next - start);

        string? nextTitle = null; int? nextTitleLevel = null;
        var current = curve.TitleForLevel(level);
        for (var l = level + 1; l <= level + 200; l++)
        {
            var t = curve.TitleForLevel(l);
            if (t != current) { nextTitle = t; nextTitleLevel = l; break; }
        }

        var streak = new StreakState(p.CurrentStreak, p.LongestStreak, p.LastQualifyingActivityDate);
        var effective = StreakRules.Effective(streak, today);
        var atRisk = effective > 0 && p.LastQualifyingActivityDate is { } last && last < today;

        return new ProgressionDto(p.TotalXp, level, current, nextTitle, nextTitleLevel, start, next, into,
            Math.Max(0, next - p.TotalXp), Math.Clamp(into / (double)span, 0, 1), effective,
            Math.Max(p.LongestStreak, effective), p.LastQualifyingActivityDate, atRisk);
    }

    public async Task<StreakUpdate> RecordQualifyingActivityAsync(Guid userId, CancellationToken ct = default)
    {
        var progress = await db.Progress.FirstOrDefaultAsync(p => p.UserId == userId, ct)
                       ?? throw new NotFoundException("User progress not found.");

        var before = new StreakState(progress.CurrentStreak, progress.LongestStreak, progress.LastQualifyingActivityDate);
        var after = StreakRules.Advance(before, challenges.TodayLocal());

        progress.CurrentStreak = after.Current;
        progress.LongestStreak = after.Longest;
        progress.LastQualifyingActivityDate = after.Last;
        return new StreakUpdate(after.Current, after.Longest, after.Last != before.Last);
    }

    public async Task<PagedResult<XpTransactionDto>> GetHistoryAsync(Guid userId, string? cursor, int pageSize, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        var query = db.XpTransactions.AsNoTracking().Where(t => t.UserId == userId);
        if (Cursor.TryDecode(cursor, out var at, out var id))
            query = query.Where(t => t.CreatedAtUtc < at || (t.CreatedAtUtc == at && t.Id.CompareTo(id) < 0));

        var rows = await query.OrderByDescending(t => t.CreatedAtUtc).ThenByDescending(t => t.Id).Take(pageSize + 1).ToListAsync(ct);
        var page = rows.Take(pageSize).ToList();
        var next = rows.Count > pageSize ? Cursor.Encode(page[^1].CreatedAtUtc, page[^1].Id) : null;
        return new PagedResult<XpTransactionDto>(
            page.Select(t => new XpTransactionDto(t.Id, t.Amount, t.SourceType.ToString(), t.SourceId, t.Reason, t.BalanceAfter, t.CreatedAtUtc)).ToList(),
            next);
    }
}
