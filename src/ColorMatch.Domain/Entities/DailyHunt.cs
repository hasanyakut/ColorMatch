using ColorMatch.Domain.Enums;

namespace ColorMatch.Domain.Entities;

/// <summary>One of the three target colours of a game day. Unique per (ChallengeDate, Difficulty).</summary>
public class DailyHunt
{
    public int Id { get; set; }
    /// <summary>Game day in the configured game time zone.</summary>
    public DateOnly ChallengeDate { get; set; }
    public HuntDifficulty Difficulty { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TargetColorHex { get; set; } = string.Empty;
    /// <summary>XP for the first valid completion. Copied from the balance when the day is created, so re-balancing never changes a running day.</summary>
    public int BaseXp { get; set; }
    /// <summary>Minimum score that counts as completed (copied from the balance for the same reason).</summary>
    public int MinScore { get; set; }
}
