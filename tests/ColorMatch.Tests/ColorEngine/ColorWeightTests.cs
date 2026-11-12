using ColorMatch.ColorEngine.ColorAnalysis;
using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.ColorEngine.ColorScoring;

namespace ColorMatch.Tests.ColorEngine;

/// <summary>The colour-weight rule: a colour that covers little of the picture scores less, so one tile of a chart or collage cannot be farmed.</summary>
public class ColorWeightTests
{
    private static readonly Rgb Grey = new(128, 128, 128);

    private static Rgb[] Grid(int w, int h, Rgb background, params (int X, int Y, int W, int H, Rgb Colour)[] rects)
    {
        var px = Enumerable.Repeat(background, w * h).ToArray();
        foreach (var r in rects)
            for (var y = r.Y; y < r.Y + r.H && y < h; y++)
                for (var x = r.X; x < r.X + r.W && x < w; x++) px[y * w + x] = r.Colour;
        return px;
    }

    [Fact]
    public void The_factor_rises_with_the_share_and_is_capped()
    {
        Assert.Equal(0, ColorWeight.Factor(0));
        Assert.Equal(1, ColorWeight.Factor(ColorWeight.FullCreditShare));
        Assert.Equal(1, ColorWeight.Factor(0.9));
        var last = -1.0;
        for (var s = 0.0; s <= 0.3; s += 0.01) { var f = ColorWeight.Factor(s); Assert.True(f >= last); last = f; }
        Assert.InRange(ColorWeight.Factor(-1), 0, 0);
        Assert.InRange(ColorWeight.Factor(5), 1, 1);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.04)]
    [InlineData(0.08)]
    public void A_perfectly_matching_colour_that_covers_little_of_the_picture_does_not_reach_the_pass_line(double share)
    {
        var score = MatchScorer.Compare(Rgb.FromHex("#2E9E4F"), Rgb.FromHex("#2E9E4F"), share).Score;
        Assert.True(score < 500, $"share {share}: score {score}");
    }

    [Fact]
    public void A_colour_that_fills_a_quarter_of_the_picture_keeps_its_full_score_and_the_old_overload_is_unchanged()
    {
        var t = Rgb.FromHex("#D6336C"); var f = Rgb.FromHex("#D8386F");
        Assert.Equal(MatchScorer.Compare(t, f).Score, MatchScorer.Compare(t, f, ColorWeight.FullCreditShare).Score);
        Assert.Equal(MatchScorer.Compare(t, f).Score, MatchScorer.Compare(t, f, 1.0).Score);
        Assert.True(MatchScorer.Compare(t, f, 0.1).Score < MatchScorer.Compare(t, f).Score);
        Assert.Equal(MatchScorer.Compare(t, f).DeltaE, MatchScorer.Compare(t, f, 0.02).DeltaE);   // ΔE stays the plain colour difference
    }

    [Fact]
    public void Share_counts_the_family_of_the_colour_and_ignores_shading_but_not_other_hues()
    {
        var green = Rgb.FromHex("#2E9E4F");
        var labs = new List<Lab>();
        labs.AddRange(Enumerable.Repeat(ColorConverter.ToLab(green), 30));
        labs.AddRange(Enumerable.Repeat(ColorConverter.ToLab(Rgb.FromHex("#1F7A3B")), 20));   // the shaded side of the same object
        labs.AddRange(Enumerable.Repeat(ColorConverter.ToLab(Rgb.FromHex("#C0392B")), 50));   // something red
        Assert.InRange(ColorWeight.Share(labs, ColorConverter.ToLab(green)), 0.49, 0.51);
        Assert.Equal(0, ColorWeight.Share([], ColorConverter.ToLab(green)));
    }

    [Fact]
    public void A_big_area_of_a_nearly_right_colour_beats_a_tiny_speck_of_the_exact_one()
    {
        var target = Rgb.FromHex("#2E9E4F");
        var near = new Rgb(60, 150, 85);                 // not exact, but it fills the picture
        var px = Grid(100, 80, Grey, (0, 0, 70, 60, near), (90, 70, 4, 4, target));
        var r = RegionFinder.Find(100, 80, px, target);
        Assert.True(r.X < 0.8 && r.Y < 0.8, $"found the speck instead of the big area: ({r.X:0.00}, {r.Y:0.00})");
        Assert.True(r.ColorShare > 0.3, $"share {r.ColorShare}");
    }

    [Fact]
    public void One_tile_of_a_colour_chart_gets_a_small_share_and_so_cannot_pass()
    {
        // 6×4 chart of different colours; one tile is exactly the target
        var target = Rgb.FromHex("#2E9E4F");
        var tiles = new[] { "#D6336C", "#1E6FD9", "#FFD23F", "#F57C00", "#7B3FB5", "#2E9E4F", "#C0392B", "#16A085", "#8E44AD", "#34495E", "#E67E22", "#95A5A6",
                            "#E84393", "#00CEC9", "#6C5CE7", "#FDCB6E", "#55EFC4", "#B2BEC3", "#FF7675", "#0984E3", "#636E72", "#A29BFE", "#FAB1A0", "#2D3436" };
        const int tile = 20;
        var px = new Rgb[6 * tile * 4 * tile];
        for (var y = 0; y < 4 * tile; y++)
            for (var x = 0; x < 6 * tile; x++) px[y * 6 * tile + x] = Rgb.FromHex(tiles[(y / tile) * 6 + x / tile]);
        var r = RegionFinder.Find(6 * tile, 4 * tile, px, target);
        Assert.InRange(r.ColorShare, 0.02, 0.12);
        Assert.True(MatchScorer.Compare(target, target, r.ColorShare).Score < 500);
    }

    [Fact]
    public void A_solid_photo_of_the_colour_has_a_full_share()
    {
        var target = Rgb.FromHex("#19A974");
        var r = RegionFinder.Find(64, 64, Enumerable.Repeat(target, 64 * 64).ToArray(), target);
        Assert.True(r.ColorShare > 0.95);
        Assert.Equal(1000, MatchScorer.Compare(target, target, r.ColorShare).Score);
    }
}
