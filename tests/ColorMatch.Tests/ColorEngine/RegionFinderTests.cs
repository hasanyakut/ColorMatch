using System.Diagnostics;
using ColorMatch.ColorEngine.ColorAnalysis;
using ColorMatch.ColorEngine.ColorConversion;

namespace ColorMatch.Tests.ColorEngine;

public class RegionFinderTests
{
    private static readonly Rgb Grey = new(128, 128, 128);

    /// <summary>A w×h grid of one colour with filled rectangles on it.</summary>
    private static Rgb[] Grid(int w, int h, Rgb background, params (int X, int Y, int W, int H, Rgb Colour)[] rects)
    {
        var px = Enumerable.Repeat(background, w * h).ToArray();
        foreach (var r in rects)
            for (var y = r.Y; y < r.Y + r.H && y < h; y++)
                for (var x = r.X; x < r.X + r.W && x < w; x++) px[y * w + x] = r.Colour;
        return px;
    }

    [Fact]
    public void Finds_the_patch_of_the_target_colour_wherever_it_is()
    {
        var target = Rgb.FromHex("#D6336C");
        var px = Grid(100, 80, Grey, (66, 18, 18, 18, target));   // centre ≈ (75, 27)
        var r = RegionFinder.Find(100, 80, px, target);
        Assert.InRange(r.X, 66 / 99.0, 84 / 99.0);
        Assert.InRange(r.Y, 18 / 79.0, 36 / 79.0);
    }

    [Fact]
    public void Prefers_the_closest_colour_when_there_are_several_patches()
    {
        var target = Rgb.FromHex("#2E9E4F");
        var near = new Rgb(50, 160, 85);      // a little off
        var far = new Rgb(30, 60, 200);
        var px = Grid(120, 90, Grey, (10, 10, 20, 20, far), (80, 55, 20, 20, near), (15, 60, 20, 20, far));
        var r = RegionFinder.Find(120, 90, px, target);
        Assert.InRange(r.X, 78 / 119.0, 102 / 119.0);
        Assert.InRange(r.Y, 53 / 89.0, 77 / 89.0);
    }

    [Fact]
    public void A_single_stray_pixel_cannot_win_over_a_real_patch()
    {
        var target = Rgb.FromHex("#FF6FB5");
        var close = new Rgb(240, 110, 175);    // a few ΔE away
        var px = Grid(100, 100, Grey, (60, 60, 16, 16, close));
        px[10 * 100 + 10] = target;            // one exact pixel, far from the patch
        var r = RegionFinder.Find(100, 100, px, target);
        Assert.InRange(r.X, 0.5, 0.85);
        Assert.InRange(r.Y, 0.5, 0.85);
    }

    [Fact]
    public void The_result_is_always_inside_the_image_with_a_usable_radius()
    {
        foreach (var (w, h) in new[] { (100, 60), (60, 100), (128, 128), (9, 9), (5, 5), (1, 1), (200, 3) })
        {
            var px = Grid(w, h, new Rgb(90, 20, 200));
            var r = RegionFinder.Find(w, h, px, Rgb.FromHex("#19A974"));
            Assert.InRange(r.X, 0, 1); Assert.InRange(r.Y, 0, 1); Assert.InRange(r.Radius, 0.005, 0.25);
        }
    }

    [Fact]
    public void Is_deterministic_and_fast()
    {
        var rng = new Random(7);
        var px = Enumerable.Range(0, 128 * 96).Select(_ => new Rgb((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256))).ToArray();
        var target = Rgb.FromHex("#A3722F");
        var sw = Stopwatch.StartNew();
        var a = RegionFinder.Find(128, 96, px, target);
        sw.Stop();
        Assert.Equal(a, RegionFinder.Find(128, 96, px, target));
        Assert.True(sw.ElapsedMilliseconds < 1500, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void A_pixel_array_that_does_not_match_the_size_is_rejected()
        => Assert.Throws<ArgumentException>(() => RegionFinder.Find(10, 10, new Rgb[50], Grey));
}
