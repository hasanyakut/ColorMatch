using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using ColorMatch.Domain.Entities;
using ColorMatch.Domain.Enums;
using ColorMatch.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ColorMatch.Application.Services;

/// <summary>
/// Color Atlas. The catalogue is defined on the server (<see cref="AtlasCatalog"/>); a colour is unlocked only as a side effect of a valid
/// daily-hunt attempt on that colour, in the same unit of work as the attempt. Finishing a collection pays a one-time reward guarded by an
/// idempotency key. Locked colours are not revealed to the client (except today's hunt targets, which the client already knows).
/// </summary>
public sealed class AtlasService(
    IAppDbContext db,
    IRewardService rewards,
    IChallengeService challenges,
    IOptions<GameBalance> balanceOptions,
    TimeProvider clock) : IAtlasService
{
    private AtlasBalance Balance => balanceOptions.Value.Atlas;

    public static string CompletionKey(string collectionKey, Guid userId) => $"atlas-complete:{collectionKey}:{userId:N}";

    public async Task<AtlasDto> GetAsync(Guid userId, CancellationToken ct)
    {
        var mine = await db.UserAtlasColors.AsNoTracking().Where(u => u.UserId == userId)
            .Select(u => new { u.ColorKey, u.UnlockedAtUtc, u.Score }).ToListAsync(ct);
        var byKey = mine.ToDictionary(m => m.ColorKey);

        var today = challenges.TodayLocal();
        var todays = await db.DailyHunts.AsNoTracking().Where(h => h.ChallengeDate == today).Select(h => h.TargetColorHex).ToListAsync(ct);
        var todaysSet = todays.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var paid = await db.XpTransactions.AsNoTracking()
            .Where(t => t.UserId == userId && t.SourceType == XpSourceType.AtlasCollectionComplete).Select(t => t.SourceId).ToListAsync(ct);
        var paidSet = paid.ToHashSet();

        var collections = AtlasCatalog.Collections.Select(c =>
        {
            var colors = c.Colors.Select(col =>
            {
                var has = byKey.TryGetValue(col.Key, out var u);
                var isToday = todaysSet.Contains(col.Hex);
                var reveal = has || isToday;
                return new AtlasColorDto(col.Key, reveal ? col.Name : null, reveal ? col.Hex : null, has, has ? u!.UnlockedAtUtc : null,
                    has ? u!.Score : null, isToday, col.Difficulty.ToString());
            }).ToList();
            var unlocked = colors.Count(x => x.Unlocked);
            return new AtlasCollectionDto(c.Key, c.Name, c.Description, unlocked, c.Colors.Count, unlocked == c.Colors.Count,
                paidSet.Contains(c.Key), Balance.CollectionCompleteXp, colors);
        }).ToList();

        return new AtlasDto(collections.Sum(c => c.Unlocked), AtlasCatalog.All.Count, collections);
    }

    public async Task<AtlasUnlockStaged?> StageUnlockAsync(Guid userId, string targetHex, Guid attemptId, int score, CancellationToken ct)
    {
        var color = AtlasCatalog.FindByHex(targetHex);
        if (color is null) return null;   // a hunt created before the atlas existed: nothing to unlock

        var already = await db.UserAtlasColors.AsNoTracking().AnyAsync(u => u.UserId == userId && u.ColorKey == color.Key, ct);
        if (already) return null;

        db.UserAtlasColors.Add(new UserAtlasColor
        {
            Id = Guid.NewGuid(), UserId = userId, ColorKey = color.Key, CollectionKey = color.CollectionKey,
            AttemptId = attemptId, Score = score, UnlockedAtUtc = clock.GetUtcNow().UtcDateTime,
        });

        var collection = AtlasCatalog.Collection(color.CollectionKey)!;
        var unlockedBefore = await db.UserAtlasColors.AsNoTracking().CountAsync(u => u.UserId == userId && u.CollectionKey == color.CollectionKey, ct);
        var unlocked = unlockedBefore + 1;
        var completed = unlocked >= collection.Colors.Count;

        RewardResult? reward = null;
        if (completed && Balance.CollectionCompleteXp > 0)
        {
            reward = await rewards.GrantAsync(new RewardRequest(userId, Balance.CollectionCompleteXp, XpSourceType.AtlasCollectionComplete,
                collection.Key, $"Color Atlas completed: {collection.Name}", CompletionKey(collection.Key, userId)), ct);
        }

        var dto = new AtlasUnlockDto(color.Key, color.Name, color.Hex, collection.Key, collection.Name, unlocked, collection.Colors.Count,
            completed, reward is { Granted: true } ? Balance.CollectionCompleteXp : 0);
        return new AtlasUnlockStaged(dto, reward);
    }
}
