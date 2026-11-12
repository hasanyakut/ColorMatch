using ColorMatch.ColorEngine.ColorConversion;

namespace ColorMatch.ColorEngine.ColorAnalysis;

/// <summary>One direction in which a found colour should move to get closer to its target.</summary>
public enum ColorTip { Lighter, Darker, MoreVivid, Duller, Redder, Greener, Yellower, Bluer }

/// <summary>
/// Turns "your colour is ΔE 12 away" into something a person can act on: compares target and found colour in CIELAB and names the
/// one or two differences that matter most (lightness, saturation, and the red–green / yellow–blue direction of the hue).
/// Language-neutral: callers translate the <see cref="ColorTip"/> values.
/// </summary>
public static class ColorAdvice
{
    /// <summary>A lightness difference below this (in L*) is not worth mentioning.</summary>
    public const double LightnessThreshold = 5.0;
    public const double ChromaThreshold = 7.0;
    public const double HueAxisThreshold = 5.0;
    public const int MaxTips = 2;

    /// <summary>The most important tips first; empty when the colours are already close in every respect.</summary>
    public static IReadOnlyList<ColorTip> For(Rgb target, Rgb found)
    {
        var t = ColorConverter.ToLab(target);
        var f = ColorConverter.ToLab(found);
        var candidates = new List<(ColorTip Tip, double Weight)>();

        var dL = t.L - f.L;
        if (Math.Abs(dL) >= LightnessThreshold) candidates.Add((dL > 0 ? ColorTip.Lighter : ColorTip.Darker, Math.Abs(dL) / LightnessThreshold));

        var dC = t.Chroma - f.Chroma;
        if (Math.Abs(dC) >= ChromaThreshold) candidates.Add((dC > 0 ? ColorTip.MoreVivid : ColorTip.Duller, Math.Abs(dC) / ChromaThreshold));

        // hue direction: only the stronger of the two opponent axes, and only when it is not just "more / less saturated" again
        var da = t.A - f.A; var db = t.B - f.B;
        var axis = Math.Abs(da) >= Math.Abs(db)
            ? (Tip: da > 0 ? ColorTip.Redder : ColorTip.Greener, Size: Math.Abs(da))
            : (Tip: db > 0 ? ColorTip.Yellower : ColorTip.Bluer, Size: Math.Abs(db));
        var hueShift = HueDistance(t, f);
        if (axis.Size >= HueAxisThreshold && hueShift >= 4.0) candidates.Add((axis.Tip, axis.Size / HueAxisThreshold));

        return candidates.OrderByDescending(c => c.Weight).Take(MaxTips).Select(c => c.Tip).ToList();
    }

    /// <summary>How far the hue itself moved (Lab distance left after removing the pure chroma change); near zero for a pure saturation change.</summary>
    private static double HueDistance(Lab a, Lab b)
    {
        var da = a.A - b.A; var db = a.B - b.B; var dC = a.Chroma - b.Chroma;
        return Math.Sqrt(Math.Max(0, da * da + db * db - dC * dC));
    }
}
