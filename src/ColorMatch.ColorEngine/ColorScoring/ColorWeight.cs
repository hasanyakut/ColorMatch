using ColorMatch.ColorEngine.ColorConversion;

namespace ColorMatch.ColorEngine.ColorScoring;

/// <summary>
/// How much of a photo is "this colour": the share of its pixels that belong to the colour family of the measured spot.
/// <para>A tiny patch of the right colour in a busy picture (a colour chart, a collage, a rainbow of tiles) used to score as high as a
/// whole object of that colour. The score is therefore multiplied by <see cref="Factor"/>: full credit needs the colour to cover at least
/// <see cref="FullCreditShare"/> of the picture, less coverage shrinks the score smoothly, and a speck cannot reach the pass line.</para>
/// <para>"Same family" is measured in CIELAB with lightness counting only half, so an object that is lit unevenly (light and shaded side) still
/// counts as one colour, while a different hue or a clearly different saturation does not.</para>
/// </summary>
public static class ColorWeight
{
    /// <summary>Share of the picture that earns the full score.</summary>
    public const double FullCreditShare = 0.25;
    /// <summary>Below the full-credit share the factor falls with this power (1 = linearly; smaller = gentler).</summary>
    public const double Exponent = 0.7;
    /// <summary>Lightness differences count this much less than hue / saturation differences when deciding "same colour".</summary>
    public const double LightnessWeight = 0.5;
    /// <summary>Distance in the weighted Lab space below which a pixel belongs to the colour.</summary>
    public const double Tolerance = 14.0;

    /// <summary>0..1 multiplier for a given share of the picture.</summary>
    public static double Factor(double share)
        => Math.Pow(Math.Clamp(share / FullCreditShare, 0.0, 1.0), Exponent);

    /// <summary>The weighted distance used for "same colour".</summary>
    public static double Distance(Lab a, Lab b)
    {
        var dl = (a.L - b.L) * LightnessWeight;
        var da = a.A - b.A; var db = a.B - b.B;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    /// <summary>Fraction (0..1) of <paramref name="pixels"/> within <see cref="Tolerance"/> of <paramref name="centre"/>.</summary>
    public static double Share(IReadOnlyList<Lab> pixels, Lab centre)
    {
        if (pixels.Count == 0) return 0;
        var n = 0;
        var tol2 = Tolerance * Tolerance;
        for (var i = 0; i < pixels.Count; i++)
        {
            var p = pixels[i];
            var dl = (p.L - centre.L) * LightnessWeight; var da = p.A - centre.A; var db = p.B - centre.B;
            if (dl * dl + da * da + db * db <= tol2) n++;
        }
        return n / (double)pixels.Count;
    }
}
