namespace ColorMatch.Domain.Entities;

/// <summary>
/// The player's progression record: XP, level and streak. Written only by the progression/reward services
/// (never from client input).
/// </summary>
public class UserProgress
{
    public Guid UserId { get; set; }
    public int TotalXp { get; set; }

    /// <summary>Derived from <see cref="TotalXp"/> by <c>LevelCurve</c> and stored so it can be queried and ranked.</summary>
    public int Level { get; set; } = 1;

    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    /// <summary>Game day (server time zone) of the last qualifying activity.</summary>
    public DateOnly? LastQualifyingActivityDate { get; set; }

    /// <summary>Optimistic concurrency token: guards against double-awarding XP from parallel requests.</summary>
    public byte[] RowVersion { get; set; } = [];
}
