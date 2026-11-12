using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.ColorEngine.ColorScoring;

namespace ColorMatch.ColorEngine.ColorAnalysis;

/// <summary>
/// Where the finder decided to measure: centre (0..1 of the width / height), radius (fraction of the shorter side) and
/// <see cref="ColorShare"/>, the share (0..1) of the whole picture that belongs to the colour found there (see <see cref="ColorWeight"/>).
/// </summary>
public sealed record FoundRegion(double X, double Y, double Radius, double ColorShare = 1.0);

/// <summary>
/// Finds, without any help from the player, the place of a photo that best represents a target colour — and how much of the picture is that colour.
/// <para>The photo is searched as a small grid (the caller down-scales it to at most <see cref="MaxEdge"/> pixels). A square window of
/// <see cref="WindowRadius"/> (fraction of the shorter side) is slid over every position; with integral images each window's mean Lab colour and
/// its spread cost O(1). A window is first ranked by <c>ΔE00(mean, target) + <see cref="SpreadPenalty"/> · spread</c>, so a flat patch of the right
/// colour beats a window that only averages to it out of unrelated colours (an edge between red and blue is not purple), and one stray pixel
/// can never win.</para>
/// <para>The best few distinct windows (<see cref="Candidates"/>) are then judged by what a player is really being asked for: a colour that
/// fills the picture. Each candidate's score estimate is <c>exp(−ΔE00/15) · <see cref="ColorWeight.Factor"/>(share of the picture in that colour)</c>,
/// so a large area of a nearly right colour beats a tiny speck of the exact one, and a colour chart or a collage cannot be farmed with one tile.</para>
/// <para>The same code runs on the server (official score) and on the phone (live estimate); the official colour is then read from the full
/// resolution photo inside the returned circle with <see cref="RegionColorAnalyzer"/>.</para>
/// </summary>
public static class RegionFinder
{
    public const int MaxEdge = 128;
    public const double WindowRadius = 0.07;
    public const double SpreadPenalty = 0.6;
    /// <summary>How many distinct best windows are compared by estimated score.</summary>
    public const int Candidates = 12;

    public static FoundRegion Find(int width, int height, IReadOnlyList<Rgb> pixels, Rgb target)
    {
        if (width < 1 || height < 1 || pixels.Count != width * height) throw new ArgumentException("The pixel grid does not match its size.", nameof(pixels));
        var shorter = Math.Min(width, height);
        var half = Math.Max(1, (int)Math.Round(WindowRadius * shorter));
        half = Math.Min(half, (shorter - 1) / 2);
        var radius = Math.Clamp(Math.Max(1, half) / (double)shorter, 0.005, 0.25);

        var labs = new Lab[pixels.Count];
        for (var i = 0; i < labs.Length; i++) labs[i] = ColorConverter.ToLab(pixels[i]);

        if (half == 0 || width <= 2 * half || height <= 2 * half) // tiny image: the whole picture is the window
            return new FoundRegion(0.5, 0.5, radius, ColorWeight.Share(labs, Mean(labs)));

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
                var lab = labs[y * width + x];
                rl += lab.L; ra += lab.A; rb += lab.B; rq += lab.L * lab.L + lab.A * lab.A + lab.B * lab.B;
                var i = (y + 1) * stride + x + 1; var up = y * stride + x + 1;
                iL[i] = iL[up] + rl; iA[i] = iA[up] + ra; iB[i] = iB[up] + rb; iQ[i] = iQ[up] + rq;
            }
        }

        double Sum(double[] t, int x0, int y0, int x1, int y1) => t[(y1 + 1) * stride + x1 + 1] - t[y0 * stride + x1 + 1] - t[(y1 + 1) * stride + x0] + t[y0 * stride + x0];

        var targetLab = ColorConverter.ToLab(target);
        var side = 2 * half + 1; var n = (double)side * side;
        var cols = width - 2 * half; var rows = height - 2 * half;
        var cost = new double[cols * rows];
        var deltaE = new double[cols * rows];
        var mean = new Lab[cols * rows];
        for (var cy = half; cy < height - half; cy++)
            for (var cx = half; cx < width - half; cx++)
            {
                int x0 = cx - half, y0 = cy - half, x1 = cx + half, y1 = cy + half;
                var mL = Sum(iL, x0, y0, x1, y1) / n; var mA = Sum(iA, x0, y0, x1, y1) / n; var mB = Sum(iB, x0, y0, x1, y1) / n;
                var variance = Math.Max(0, Sum(iQ, x0, y0, x1, y1) / n - (mL * mL + mA * mA + mB * mB));
                var idx = (cy - half) * cols + (cx - half);
                var m = new Lab(mL, mA, mB);
                mean[idx] = m;
                deltaE[idx] = Ciede2000.Difference(targetLab, m);
                cost[idx] = deltaE[idx] + SpreadPenalty * Math.Sqrt(variance);
            }

        // the best few windows that are not just neighbours of each other
        var order = Enumerable.Range(0, cost.Length).OrderBy(i => cost[i]).ToArray();
        var picked = new List<int>(Candidates);
        var minGap = 2 * half;
        foreach (var i in order)
        {
            int ix = i % cols, iy = i / cols;
            if (picked.Any(p => Math.Abs(p % cols - ix) < minGap && Math.Abs(p / cols - iy) < minGap)) continue;
            picked.Add(i);
            if (picked.Count == Candidates) break;
        }

        var best = picked[0]; var bestShare = 0.0; var bestEstimate = -1.0;
        foreach (var i in picked)
        {
            var share = ColorWeight.Share(labs, mean[i]);
            var estimate = Math.Exp(-deltaE[i] / MatchScorer.DecayConstant) * ColorWeight.Factor(share);
            if (estimate > bestEstimate + 1e-9) { bestEstimate = estimate; best = i; bestShare = share; }
        }

        var bx = best % cols + half; var by = best / cols + half;
        return new FoundRegion(width > 1 ? bx / (double)(width - 1) : 0.5, height > 1 ? by / (double)(height - 1) : 0.5, radius, bestShare);
    }

    private static Lab Mean(Lab[] labs)
    {
        double l = 0, a = 0, b = 0;
        foreach (var p in labs) { l += p.L; a += p.A; b += p.B; }
        return new Lab(l / labs.Length, a / labs.Length, b / labs.Length);
    }
}
