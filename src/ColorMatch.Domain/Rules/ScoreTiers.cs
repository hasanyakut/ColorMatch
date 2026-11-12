using ColorMatch.Domain.Enums;

namespace ColorMatch.Domain.Rules;

/// <summary>
/// Score bands: Perfect ≥ 950 · Excellent ≥ 850 · Great ≥ 700 · Good ≥ 500 · Needs improvement below.
/// With the scorer's curve (score = 1000·e^(−ΔE/15)) these bands are ΔE00 ≤ 0.8, ≤ 2.4, ≤ 5.3 and ≤ 10.4 respectively;
/// ΔE00 ≈ 2.3 is the smallest difference a human notices, so "Perfect" really means indistinguishable.
/// </summary>
public static class ScoreTiers
{
    public static ScoreTier For(int score) => score switch
    {
        >= 950 => ScoreTier.Perfect,
        >= 850 => ScoreTier.Excellent,
        >= 700 => ScoreTier.Great,
        >= 500 => ScoreTier.Good,
        _ => ScoreTier.NeedsImprovement,
    };
}
