namespace ColorMatch.Domain.Entities;

public class DailyHuntAttempt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int DailyHuntId { get; set; }
    public DailyHunt? DailyHunt { get; set; }

    public int Score { get; set; }
    public double DeltaE { get; set; }
    public string SampledColorHex { get; set; } = string.Empty;
    /// <summary>True when the score reached the hunt's minimum (stored, so later re-balancing does not rewrite history).</summary>
    public bool Passed { get; set; }
    /// <summary>XP paid for this attempt (non-zero only on the first valid completion of the hunt).</summary>
    public int XpAwarded { get; set; }

    public double RegionX { get; set; }
    public double RegionY { get; set; }
    public double RegionRadius { get; set; }

    /// <summary>Storage key of the normalised photo.</summary>
    public string PhotoKey { get; set; } = string.Empty;
    /// <summary>SHA-256 of the normalised JPEG (basic re-use check; see the security notes).</summary>
    public string PhotoHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
