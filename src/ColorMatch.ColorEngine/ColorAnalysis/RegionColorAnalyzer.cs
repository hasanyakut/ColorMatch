using ColorMatch.ColorEngine.ColorConversion;

namespace ColorMatch.ColorEngine.ColorAnalysis;

/// <summary>Outcome of analysing the pixels of a selected region.</summary>
public sealed record RegionAnalysis(Rgb Color, int TotalPixels, int UsedPixels, int DiscardedHighlights, int DiscardedShadows);

/// <summary>
/// Derives one representative colour from the pixels of a user-selected region.
/// <para>Algorithm (deterministic):</para>
/// <list type="number">
/// <item>Convert every pixel to CIELAB.</item>
/// <item>Discard specular highlights (L* &gt; 94 with low chroma) and crushed shadows (L* &lt; 6) — but only if
/// at least <see cref="MinKeepFraction"/> of the pixels survive, so genuinely white/black targets still work.</item>
/// <item>Take the per-channel median in Lab as a robust centre (immune to a few outliers).</item>
/// <item>Keep pixels within <see cref="InlierDeltaE"/> (Euclidean Lab) of the median; average them in linear Lab.</item>
/// </list>
/// </summary>
public static class RegionColorAnalyzer
{
    public const double HighlightL = 94.0;
    public const double HighlightMaxChroma = 12.0;
    public const double ShadowL = 6.0;
    public const double MinKeepFraction = 0.25;
    public const double InlierDeltaE = 18.0;
    public const int MinPixels = 1;

    public static RegionAnalysis Analyze(IReadOnlyList<Rgb> pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Count < MinPixels) throw new ArgumentException("At least one pixel is required.", nameof(pixels));

        var labs = new Lab[pixels.Count];
        for (var i = 0; i < pixels.Count; i++) labs[i] = ColorConverter.ToLab(pixels[i]);

        // 1) remove highlights / shadows when enough data remains
        var kept = new List<Lab>(labs.Length);
        int highlights = 0, shadows = 0;
        foreach (var lab in labs)
        {
            if (lab.L > HighlightL && lab.Chroma < HighlightMaxChroma) highlights++;
            else if (lab.L < ShadowL) shadows++;
            else kept.Add(lab);
        }
        if (kept.Count < Math.Ceiling(labs.Length * MinKeepFraction))
        {
            kept = [.. labs];
            highlights = shadows = 0;
        }

        // 2) robust centre
        var centre = new Lab(Median(kept, c => c.L), Median(kept, c => c.A), Median(kept, c => c.B));

        // 3) average of inliers
        double sl = 0, sa = 0, sb = 0;
        var n = 0;
        foreach (var c in kept)
        {
            if (Distance(c, centre) > InlierDeltaE) continue;
            sl += c.L; sa += c.A; sb += c.B; n++;
        }
        var mean = n > 0 ? new Lab(sl / n, sa / n, sb / n) : centre;
        if (n == 0) n = 1;

        return new RegionAnalysis(ColorConverter.ToRgb(mean), labs.Length, n, highlights, shadows);
    }

    private static double Distance(Lab a, Lab b)
    {
        var dl = a.L - b.L; var da = a.A - b.A; var db = a.B - b.B;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    private static double Median(List<Lab> values, Func<Lab, double> selector)
    {
        var arr = values.Select(selector).ToArray();
        Array.Sort(arr);
        var mid = arr.Length / 2;
        return arr.Length % 2 == 1 ? arr[mid] : (arr[mid - 1] + arr[mid]) / 2.0;
    }
}
