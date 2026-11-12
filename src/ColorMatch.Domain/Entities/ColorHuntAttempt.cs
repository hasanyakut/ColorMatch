namespace ColorMatch.Domain.Entities;

public class ColorHuntAttempt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int ChallengeId { get; set; }
    public DailyColorChallenge? Challenge { get; set; }

    public string TargetHex { get; set; } = string.Empty;
    public string FoundHex { get; set; } = string.Empty;
    public double DeltaE { get; set; }
    public int Score { get; set; }
    public int XpAwarded { get; set; }

    /// <summary>Selected sample region, normalised 0..1 (centre) and radius as a fraction of the shorter image side.</summary>
    public double RegionX { get; set; }
    public double RegionY { get; set; }
    public double RegionRadius { get; set; }

    public string PhotoKey { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
