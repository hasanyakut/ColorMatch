using ColorMatch.ColorEngine.ColorConversion;

namespace ColorMatch.ColorEngine.ColorScoring;

/// <summary>Result of comparing a target colour with a sampled colour.</summary>
public sealed record MatchResult(Rgb Target, Rgb Found, double DeltaE, int Score);

/// <summary>
/// Converts a CIEDE2000 distance into a 0–1000 score.
/// <para>
/// <c>score = round(1000 · exp(−ΔE00 / 15))</c>. The curve is strictly decreasing, equals 1000 for an
/// identical colour and is deliberately forgiving of camera / lighting error:
/// ΔE 1 (barely perceptible) ≈ 936, ΔE 3 ≈ 819, ΔE 5 ≈ 717, ΔE 10 ≈ 513, ΔE 20 ≈ 264, ΔE 40 ≈ 69.
/// The same formula runs in the mobile preview and in the authoritative server calculation.
/// </para>
/// </summary>
public static class MatchScorer
{
    public const int MaxScore = 1000;
    public const double DecayConstant = 15.0;

    public static int ScoreFromDeltaE(double deltaE)
    {
        if (double.IsNaN(deltaE) || deltaE < 0) throw new ArgumentOutOfRangeException(nameof(deltaE));
        var score = MaxScore * Math.Exp(-deltaE / DecayConstant);
        return Math.Clamp((int)Math.Round(score, MidpointRounding.AwayFromZero), 0, MaxScore);
    }

    public static MatchResult Compare(Rgb target, Rgb found)
    {
        var de = Ciede2000.Difference(target, found);
        return new MatchResult(target, found, de, ScoreFromDeltaE(de));
    }
}
