using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using ColorMatch.ColorEngine.ColorAnalysis;
using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.ColorEngine.ColorScoring;
using ColorMatch.Domain.Entities;
using ColorMatch.Domain.Enums;
using ColorMatch.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace ColorMatch.Application.Services;

public sealed class ColorHuntService(
    IAppDbContext db,
    IColorMatchService colorMatch,
    IRewardService rewards,
    IProgressionService progression,
    IChallengeService challenges,
    IFileStorage storage,
    TimeProvider clock) : IColorHuntService
{
    public async Task<HuntAttemptResultDto> SubmitAsync(HuntSubmission s, CancellationToken ct)
    {
        var challenge = await challenges.GetOrCreateTodayEntityAsync(ct);
        // Official score: computed here, never taken from the client.
        var outcome = colorMatch.Evaluate(s.Photo, challenge.Hex);
        var image = outcome.Image;
        var match = new { outcome.DeltaE, outcome.Score };

        var previous = await db.HuntAttempts.AsNoTracking()
            .Where(a => a.UserId == s.UserId && a.ChallengeId == challenge.Id)
            .GroupBy(_ => 1)
            .Select(g => new { Best = g.Max(a => a.Score), Xp = g.Sum(a => a.XpAwarded) })
            .FirstOrDefaultAsync(ct);

        var previousBest = previous?.Best ?? 0;
        var xpAward = XpRules.XpToAward(match.Score, previous?.Xp ?? 0);

        var attemptId = Guid.NewGuid();
        var photoKey = $"{MediaPaths.UserPrefix(s.UserId)}hunt/{attemptId:N}.jpg";
        await using (var ms = new MemoryStream(image.JpegBytes))
            await storage.SaveAsync(photoKey, ms, ct);

        try
        {
            var progress = await db.Progress.FirstOrDefaultAsync(p => p.UserId == s.UserId, ct)
                           ?? throw new NotFoundException("User progress not found.");

            db.HuntAttempts.Add(new ColorHuntAttempt
            {
                Id = attemptId,
                UserId = s.UserId,
                ChallengeId = challenge.Id,
                TargetHex = challenge.Hex,
                FoundHex = outcome.Found.ToHex(),
                DeltaE = Math.Round(match.DeltaE, 3),
                Score = match.Score,
                XpAwarded = xpAward,
                RegionX = outcome.Region.X,
                RegionY = outcome.Region.Y,
                RegionRadius = outcome.Region.Radius,
                PhotoKey = photoKey,
                CreatedAtUtc = clock.GetUtcNow().UtcDateTime
            });

            // XP goes through the ledger (idempotent per attempt); the progress row's rowversion serialises concurrent awards for the same user.
            if (xpAward > 0)
                await rewards.GrantAsync(new RewardRequest(s.UserId, xpAward, XpSourceType.HuntAttempt, attemptId.ToString("N"),
                    $"Colour hunt {challenge.Date:yyyy-MM-dd} (score {match.Score})", $"hunt-attempt:{attemptId:N}"), ct);
            await progression.RecordQualifyingActivityAsync(s.UserId, ct);

            await db.SaveChangesAsync(ct);

            var bestNow = Math.Max(previousBest, match.Score);
            return new HuntAttemptResultDto(attemptId, challenge.Hex, outcome.Found.ToHex(), Math.Round(match.DeltaE, 2),
                match.Score, xpAward, bestNow, match.Score > previousBest, progress.TotalXp, progress.Level,
                photoKey, MediaPaths.ToUrl(photoKey)!, outcome.RegionDto);
        }
        catch (DbUpdateConcurrencyException)
        {
            await storage.DeleteAsync(photoKey, CancellationToken.None);
            throw new ConflictException("Your progress changed at the same time. Please try again.");
        }
        catch
        {
            await storage.DeleteAsync(photoKey, CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<HuntAttemptSummaryDto>> GetMyTodayAsync(Guid userId, CancellationToken ct)
    {
        var challenge = await challenges.GetOrCreateTodayEntityAsync(ct);
        var rows = await db.HuntAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.ChallengeId == challenge.Id)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(50)
            .ToListAsync(ct);
        return rows.Select(a => new HuntAttemptSummaryDto(a.Id, a.FoundHex, a.Score, a.XpAwarded, a.PhotoKey, MediaPaths.ToUrl(a.PhotoKey)!, a.CreatedAtUtc)).ToList();
    }
}
