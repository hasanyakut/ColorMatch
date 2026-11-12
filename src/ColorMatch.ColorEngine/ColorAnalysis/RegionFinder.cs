using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.ColorEngine.ColorScoring;

namespace ColorMatch.ColorEngine.ColorAnalysis;

/// <summary>Where the finder decided to measure: centre (0..1 of the width / height) and radius (fraction of the shorter side).</summary>
public sealed record FoundRegion(double X, double Y, double Radius);

/// <summary>
/// Finds, without any help from the player, the place of a photo that best matches a target colour.
/// <para>The photo is searched as a small grid (the caller down-scales it to at most <see cref="MaxEdge"/> pixels). A square window of
/// <see cref="WindowRadius"/> (fraction of the shorter side) is slid over every position; with integral images each window's mean Lab colour and
/// its spread cost O(1). A window is scored by <c>ΔE00(mean, target) + <see cref="SpreadPenalty"/> · spread</c>, so a flat patch of the right
/// colour wins over a window that only averages to it out of unrelated colours (an edge between red and blue is not purple).
/// Because the window has a real size, one stray pixel can never win.</para>
/// <para>The same code runs on the server (official score) and on the phone (live estimate); the official colour is then read from the full
/// resolution photo inside the returned circle with <see cref="RegionColorAnalyzer"/>.</para>
/// </summary>
public static class RegionFinder
{
    public const int MaxEdge = 128;
    public const double WindowRadius = 0.07;
    public const double SpreadPenalty = 0.6;

    public static FoundRegion Find(int width, int height, IReadOnlyList<Rgb> pixels, Rgb target)
    {
        if (width < 1 || height < 1 || pixels.Count != width * height) throw new ArgumentException("The pixel grid does not match its size.", nameof(pixels));
        var shorter = Math.Min(width, height);
        var half = Math.Max(1, (int)Math.Round(WindowRadius * shorter));
        half = Math.Min(half, (shorter - 1) / 2);
        var radius = Math.Max(1, half) / (double)shorter;

        // (w+1)×(h+1) integral images of L, a, b and of L²+a²+b²
        var stride = width + 1;
        var iL = new double[stride * (height + 1)];
        var iA = new double[iL.Length];
        var iB = new double[iL.Length];
        var iQ = new double[iL.Length];
        for (var y = 0; y < height; y++)
        {
            double rl = 0, ra = 0, rb = 0, rq = 0;
            for (var x = 0; x < width; x++)
            {
                var lab = ColorConverter.ToLab(pixels[y * width + x]);
                rl += lab.L; ra += lab.A; rb += lab.B; rq += lab.L * lab.L + lab.A * lab.A + lab.B * lab.B;
                var i = (y + 1) * stride + x + 1; var up = y * stride + x + 1;
                iL[i] = iL[up] + rl; iA[i] = iA[up] + ra; iB[i] = iB[up] + rb; iQ[i] = iQ[up] + rq;
            }
        }

        double Sum(double[] t, int x0, int y0, int x1, int y1) => t[(y1 + 1) * stride + x1 + 1] - t[y0 * stride + x1 + 1] - t[(y1 + 1) * stride + x0] + t[y0 * stride + x0];

        if (half == 0 || width <= 2 * half || height <= 2 * half) // tiny image: the whole picture is the window
            return new FoundRegion(0.5, 0.5, Math.Clamp(radius, 0.005, 0.25));

        var targetLab = ColorConverter.ToLab(target);
        var bestCost = double.MaxValue; var bx = width / 2; var by = height / 2;
        var side = 2 * half + 1; var n = (double)side * side;
        for (var cy = half; cy < height - half; cy++)
            for (var cx = half; cx < width - half; cx++)
            {
                int x0 = cx - half, y0 = cy - half, x1 = cx + half, y1 = cy + half;
                var mL = Sum(iL, x0, y0, x1, y1) / n; var mA = Sum(iA, x0, y0, x1, y1) / n; var mB = Sum(iB, x0, y0, x1, y1) / n;
                var variance = Math.Max(0, Sum(iQ, x0, y0, x1, y1) / n - (mL * mL + mA * mA + mB * mB));
                var cost = Ciede2000.Difference(targetLab, new Lab(mL, mA, mB)) + SpreadPenalty * Math.Sqrt(variance);
                if (cost < bestCost) { bestCost = cost; bx = cx; by = cy; }
            }

        return new FoundRegion(width > 1 ? bx / (double)(width - 1) : 0.5, height > 1 ? by / (double)(height - 1) : 0.5, Math.Clamp(radius, 0.005, 0.25));
    }
}
