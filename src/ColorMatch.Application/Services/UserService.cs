using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using ColorMatch.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace ColorMatch.Application.Services;

public sealed class UserService(IAppDbContext db, IImageProcessor images, IFileStorage storage, IChallengeService challenges, LevelCurve curve) : IUserService
{
    private const int AvatarMaxEdge = 512;

    public async Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken ct)
    {
        var profile = await db.Profiles.AsNoTracking()
            .Where(p => p.Id == userId)
            .Select(p => new
            {
                p.Id, p.UserName, p.AvatarKey, p.CreatedAtUtc,
                Xp = p.Progress != null ? p.Progress.TotalXp : 0,
                Streak = p.Progress != null ? p.Progress.CurrentStreak : 0,
                Longest = p.Progress != null ? p.Progress.LongestStreak : 0,
                LastDay = p.Progress != null ? p.Progress.LastQualifyingActivityDate : null,
                Posts = db.Posts.Count(x => x.UserId == p.Id),
                Best = db.HuntAttempts.Where(a => a.UserId == p.Id).Max(a => (int?)a.Score) ?? 0
            })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("User not found.");

        // the streak is stored by the progression service; it is shown as 0 once its grace day has passed
        var streak = StreakRules.Effective(new StreakState(profile.Streak, profile.Longest, profile.LastDay), challenges.TodayLocal());
        var level = curve.LevelForXp(profile.Xp);
        return new UserProfileDto(profile.Id, profile.UserName, MediaPaths.ToUrl(profile.AvatarKey), profile.Xp, level,
            curve.XpForLevel(level), curve.XpForLevel(level + 1), profile.Posts, profile.Best, streak, profile.CreatedAtUtc, curve.TitleForLevel(level));
    }

    public async Task<UserProfileDto> SetAvatarAsync(Guid userId, Stream image, CancellationToken ct)
    {
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.Id == userId, ct)
                      ?? throw new NotFoundException("User not found.");

        var normalized = images.Normalize(image, AvatarMaxEdge);
        var key = $"{MediaPaths.UserPrefix(userId)}avatar/{Guid.NewGuid():N}.jpg";
        await using (var ms = new MemoryStream(normalized.JpegBytes))
            await storage.SaveAsync(key, ms, ct);

        var oldKey = profile.AvatarKey;
        profile.AvatarKey = key;
        await db.SaveChangesAsync(ct);
        if (oldKey is not null) await storage.DeleteAsync(oldKey, ct);

        return await GetProfileAsync(userId, ct);
    }
}
