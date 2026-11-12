using ColorMatch.Domain.Entities;
using ColorMatch.Domain.Enums;
using ColorMatch.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace ColorMatch.Infrastructure.Persistence;

/// <summary>
/// One-time, idempotent upgrade of players created before the game core existed:
/// (1) XP that has no ledger rows gets a single "LegacyImport" row, so total XP == sum of ledger and nobody loses XP;
/// (2) stored level, streak and last activity day are rebuilt from the hunt history.
/// Safe to run on every start: it only touches players whose ledger or streak is still unset.
/// </summary>
public static class GameCoreBackfill
{
    public static async Task RunAsync(AppDbContext db, GameBalance balance, TimeProvider clock, CancellationToken ct = default)
    {
        var curve = new LevelCurve(balance);

        // 1) ledger
        var ledgerSums = await db.XpTransactions.GroupBy(t => t.UserId).Select(g => new { UserId = g.Key, Sum = g.Sum(t => t.Amount) }).ToDictionaryAsync(x => x.UserId, x => x.Sum, ct);
        var players = await db.Progress.ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var p in players)
        {
            var booked = ledgerSums.GetValueOrDefault(p.UserId);
            if (p.TotalXp > booked)
            {
                db.XpTransactions.Add(new XpTransaction
                {
                    Id = Guid.NewGuid(), UserId = p.UserId, Amount = p.TotalXp - booked, SourceType = XpSourceType.LegacyImport,
                    Reason = "XP earned before the XP ledger existed", IdempotencyKey = $"legacy-xp:{p.UserId:N}",
                    BalanceAfter = p.TotalXp, CreatedAtUtc = now,
                });
            }
            p.Level = curve.LevelForXp(p.TotalXp);
        }

        // 2) streaks (only for players that have history but no stored streak yet)
        var needStreak = players.Where(p => p.LastQualifyingActivityDate is null).Select(p => p.UserId).ToList();
        if (needStreak.Count > 0)
        {
            var days = await db.HuntAttempts.AsNoTracking()
                .Where(a => needStreak.Contains(a.UserId))
                .Select(a => new { a.UserId, Day = a.Challenge!.Date })
                .Distinct()
                .ToListAsync(ct);
            foreach (var g in days.GroupBy(d => d.UserId))
            {
                var state = StreakRules.FromHistory(g.Select(d => d.Day));
                var p = players.First(x => x.UserId == g.Key);
                p.CurrentStreak = state.Current; p.LongestStreak = state.Longest; p.LastQualifyingActivityDate = state.Last;
            }
        }

        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
    }
}
