using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using ColorMatch.Domain.Entities;
using ColorMatch.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ColorMatch.Application.Services;

public sealed class ChallengeOptions
{
    public const string Section = "Challenge";
    /// <summary>Time zone whose midnight starts a new daily colour (same for all users).</summary>
    public string TimeZoneId { get; set; } = "Europe/Istanbul";
}

public sealed class ChallengeService : IChallengeService
{
    private readonly IAppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _zone;

    public ChallengeService(IAppDbContext db, TimeProvider clock, IOptions<ChallengeOptions> options)
    {
        _db = db;
        _clock = clock;
        try { _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _zone = TimeZoneInfo.Utc;
        }
    }

    public DateOnly TodayLocal() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _zone).DateTime);

    public async Task<DailyColorChallenge> GetOrCreateTodayEntityAsync(CancellationToken ct)
    {
        var today = TodayLocal();
        var existing = await _db.Challenges.AsNoTracking().FirstOrDefaultAsync(c => c.Date == today, ct);
        if (existing is not null) return existing;

        var pick = DailyColorPalette.ForDate(today);
        var entity = new DailyColorChallenge { Date = today, Name = pick.Name, Hex = pick.Hex };
        _db.Challenges.Add(entity);
        try
        {
            await _db.SaveChangesAsync(ct);
            return entity;
        }
        catch (DbUpdateException)
        {
            // unique(Date): another request created it first – use that row
            _db.Challenges.Remove(entity);
            return await _db.Challenges.AsNoTracking().FirstAsync(c => c.Date == today, ct);
        }
    }

    public async Task<DailyChallengeDto> GetTodayAsync(Guid? userId, CancellationToken ct)
    {
        var challenge = await GetOrCreateTodayEntityAsync(ct);

        var best = 0; var xp = 0; var attempts = 0;
        if (userId is { } uid)
        {
            var stats = await _db.HuntAttempts.AsNoTracking()
                .Where(a => a.UserId == uid && a.ChallengeId == challenge.Id)
                .GroupBy(_ => 1)
                .Select(g => new { Best = g.Max(a => a.Score), Xp = g.Sum(a => a.XpAwarded), Count = g.Count() })
                .FirstOrDefaultAsync(ct);
            if (stats is not null) { best = stats.Best; xp = stats.Xp; attempts = stats.Count; }
        }

        return new DailyChallengeDto(challenge.Id, challenge.Date, challenge.Name, challenge.Hex, NextResetUtc(challenge.Date), best, xp, attempts);
    }

    public async Task<LeaderboardDto> GetLeaderboardAsync(Guid userId, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 50);
        var challenge = await GetOrCreateTodayEntityAsync(ct);

        var best = _db.HuntAttempts.AsNoTracking()
            .Where(a => a.ChallengeId == challenge.Id)
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, Best = g.Max(a => a.Score) });

        var total = await best.CountAsync(ct);
        var top = await best
            .OrderByDescending(x => x.Best).ThenBy(x => x.UserId)
            .Take(take)
            .Join(_db.Profiles.AsNoTracking(), x => x.UserId, p => p.Id, (x, p) => new { x.UserId, p.UserName, p.AvatarKey, x.Best })
            .OrderByDescending(x => x.Best).ThenBy(x => x.UserId)
            .ToListAsync(ct);

        var entries = top.Select((x, i) => new LeaderboardEntryDto(i + 1, x.UserId, x.UserName,
            Common.MediaPaths.ToUrl(x.AvatarKey), x.Best, x.UserId == userId)).ToList();

        int? myRank = entries.FirstOrDefault(e => e.IsMe)?.Rank;
        if (myRank is null)
        {
            var mine = await _db.HuntAttempts.AsNoTracking()
                .Where(a => a.ChallengeId == challenge.Id && a.UserId == userId)
                .MaxAsync(a => (int?)a.Score, ct);
            if (mine is { } m)
                myRank = 1 + await best.CountAsync(x => x.Best > m, ct);
        }
        return new LeaderboardDto(entries, myRank, total);
    }

    public async Task<IReadOnlyList<PastChallengeDto>> GetRecentAsync(Guid userId, int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 14);
        var today = TodayLocal();
        var from = today.AddDays(-days);

        var rows = await _db.Challenges.AsNoTracking()
            .Where(c => c.Date >= from && c.Date < today)
            .Select(c => new { c.Date, c.Id, Best = _db.HuntAttempts.Where(a => a.ChallengeId == c.Id && a.UserId == userId).Max(a => (int?)a.Score) })
            .ToListAsync(ct);
        var bestByDate = rows.ToDictionary(r => r.Date, r => r.Best ?? 0);

        // the colour of a day is a pure function of the date, so days without a stored row are still shown
        var result = new List<PastChallengeDto>();
        for (var d = today.AddDays(-1); d >= from; d = d.AddDays(-1))
        {
            var pick = DailyColorPalette.ForDate(d);
            result.Add(new PastChallengeDto(d, pick.Name, pick.Hex, bestByDate.GetValueOrDefault(d)));
        }
        return result;
    }

    public DateTime NextResetUtc(DateOnly date)
    {
        var nextMidnightLocal = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(nextMidnightLocal, _zone);
    }
}
