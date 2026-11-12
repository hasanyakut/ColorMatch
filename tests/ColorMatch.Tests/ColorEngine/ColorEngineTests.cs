using ColorMatch.ColorEngine.ColorAnalysis;
using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.ColorEngine.ColorScoring;

namespace ColorMatch.Tests.ColorEngine;

public class ColorEngineTests
{
    // Reference data: Sharma, Wu & Dalal (2005) test set (33 pairs reproduced here; pair 33 omitted - its inputs could not be verified offline).
    public static IEnumerable<object[]> SharmaPairs() =>
    [
        [50.0000, 2.6772, -79.7751, 50.0000, 0.0000, -82.7485, 2.0425],
        [50.0000, 3.1571, -77.2803, 50.0000, 0.0000, -82.7485, 2.8615],
        [50.0000, 2.8361, -74.0200, 50.0000, 0.0000, -82.7485, 3.4412],
        [50.0000, -1.3802, -84.2814, 50.0000, 0.0000, -82.7485, 1.0000],
        [50.0000, -1.1848, -84.8006, 50.0000, 0.0000, -82.7485, 1.0000],
        [50.0000, -0.9009, -85.5211, 50.0000, 0.0000, -82.7485, 1.0000],
        [50.0000, 0.0000, 0.0000, 50.0000, -1.0000, 2.0000, 2.3669],
        [50.0000, -1.0000, 2.0000, 50.0000, 0.0000, 0.0000, 2.3669],
        [50.0000, 2.4900, -0.0010, 50.0000, -2.4900, 0.0009, 7.1792],
        [50.0000, 2.4900, -0.0010, 50.0000, -2.4900, 0.0010, 7.1792],
        [50.0000, 2.4900, -0.0010, 50.0000, -2.4900, 0.0011, 7.2195],
        [50.0000, 2.4900, -0.0010, 50.0000, -2.4900, 0.0012, 7.2195],
        [50.0000, -0.0010, 2.4900, 50.0000, 0.0009, -2.4900, 4.8045],
        [50.0000, -0.0010, 2.4900, 50.0000, 0.0010, -2.4900, 4.8045],
        [50.0000, -0.0010, 2.4900, 50.0000, 0.0011, -2.4900, 4.7461],
        [50.0000, 2.5000, 0.0000, 50.0000, 0.0000, -2.5000, 4.3065],
        [50.0000, 2.5000, 0.0000, 73.0000, 25.0000, -18.0000, 27.1492],
        [50.0000, 2.5000, 0.0000, 61.0000, -5.0000, 29.0000, 22.8977],
        [50.0000, 2.5000, 0.0000, 56.0000, -27.0000, -3.0000, 31.9030],
        [50.0000, 2.5000, 0.0000, 58.0000, 24.0000, 15.0000, 19.4535],
        [50.0000, 2.5000, 0.0000, 50.0000, 3.1736, 0.5854, 1.0000],
        [50.0000, 2.5000, 0.0000, 50.0000, 3.2972, 0.0000, 1.0000],
        [50.0000, 2.5000, 0.0000, 50.0000, 1.8634, 0.5757, 1.0000],
        [50.0000, 2.5000, 0.0000, 50.0000, 3.2592, 0.3350, 1.0000],
        [60.2574, -34.0099, 36.2677, 60.4626, -34.1751, 39.4387, 1.2644],
        [63.0109, -31.0961, -5.8663, 62.8187, -29.7946, -4.0864, 1.2630],
        [61.2901, 3.7196, -5.3901, 61.4292, 2.2480, -4.9620, 1.8731],
        [35.0831, -44.1164, 3.7933, 35.0232, -40.0716, 1.5901, 1.8645],
        [22.7233, 20.0904, -46.6940, 23.0331, 14.9730, -42.5619, 2.0373],
        [36.4612, 47.8580, 18.3852, 36.2715, 50.5065, 21.2231, 1.4146],
        [90.8027, -2.0831, 1.4410, 91.1528, -1.6435, 0.0447, 1.4441],
        [90.9257, -0.5406, -0.9208, 88.6381, -0.8985, -0.7239, 1.5381],
        [2.0776, 0.0795, -1.1350, 0.9033, -0.0636, -0.5514, 0.9082],
    ];

    [Theory]
    [MemberData(nameof(SharmaPairs))]
    public void Ciede2000_matches_published_reference(double l1, double a1, double b1, double l2, double a2, double b2, double expected)
    {
        var de = Ciede2000.Difference(new Lab(l1, a1, b1), new Lab(l2, a2, b2));
        Assert.Equal(expected, de, 4);
    }

    [Fact]
    public void Ciede2000_is_symmetric_and_zero_for_identical()
    {
        var a = ColorConverter.ToLab(Rgb.FromHex("#E86C52"));
        var b = ColorConverter.ToLab(Rgb.FromHex("#3366CC"));
        Assert.Equal(0, Ciede2000.Difference(a, a), 10);
        Assert.Equal(Ciede2000.Difference(a, b), Ciede2000.Difference(b, a), 6);
    }

    [Theory]
    [InlineData("#FFFFFF", 100.0, 0.0, 0.0)]
    [InlineData("#000000", 0.0, 0.0, 0.0)]
    [InlineData("#FF0000", 53.2408, 80.0925, 67.2032)]
    [InlineData("#00FF00", 87.7347, -86.1827, 83.1793)]
    [InlineData("#0000FF", 32.2970, 79.1875, -107.8602)]
    public void Srgb_to_Lab_matches_known_values(string hex, double l, double a, double b)
    {
        var lab = ColorConverter.ToLab(Rgb.FromHex(hex));
        Assert.Equal(l, lab.L, 1);
        Assert.Equal(a, lab.A, 1);
        Assert.Equal(b, lab.B, 1);
    }

    [Fact]
    public void Rgb_Lab_roundtrip_is_lossless_for_8bit_values()
    {
        var rnd = new Random(42);
        for (var i = 0; i < 2000; i++)
        {
            var c = new Rgb((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256));
            Assert.Equal(c, ColorConverter.ToRgb(ColorConverter.ToLab(c)));
        }
    }

    [Theory]
    [InlineData("#E86C52", 232, 108, 82)]
    [InlineData("e86c52", 232, 108, 82)]
    [InlineData("#fff", 255, 255, 255)]
    public void Hex_parsing(string hex, byte r, byte g, byte b)
    {
        Assert.True(Rgb.TryParseHex(hex, out var rgb));
        Assert.Equal(new Rgb(r, g, b), rgb);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData(null)]
    public void Hex_parsing_rejects_garbage(string? hex) => Assert.False(Rgb.TryParseHex(hex, out _));

    [Fact]
    public void Hex_format_roundtrip() => Assert.Equal("#E86C52", Rgb.FromHex("#e86c52").ToHex());

    [Fact]
    public void Score_is_1000_for_identical_and_monotonic()
    {
        Assert.Equal(1000, MatchScorer.ScoreFromDeltaE(0));
        var prev = 1000;
        for (double de = 0.5; de < 100; de += 0.5)
        {
            var s = MatchScorer.ScoreFromDeltaE(de);
            Assert.InRange(s, 0, prev);
            prev = s;
        }
    }

    [Theory]
    [InlineData(1.0, 936)]
    [InlineData(5.0, 717)]
    [InlineData(10.0, 513)]
    [InlineData(40.0, 69)]
    public void Score_curve_anchor_points(double de, int expected) =>
        Assert.Equal(expected, MatchScorer.ScoreFromDeltaE(de));

    [Fact]
    public void Compare_example_from_spec_scores_high()
    {
        var r = MatchScorer.Compare(Rgb.FromHex("#E86C52"), Rgb.FromHex("#E67258"));
        Assert.InRange(r.DeltaE, 0.5, 4);
        Assert.InRange(r.Score, 780, 980);
    }

    [Fact]
    public void Compare_opposite_colours_score_low()
    {
        var r = MatchScorer.Compare(Rgb.FromHex("#FF0000"), Rgb.FromHex("#00FFFF"));
        Assert.True(r.Score < 100);
    }

    [Fact]
    public void Analyzer_uniform_region_returns_that_colour()
    {
        var target = Rgb.FromHex("#3A7BD5");
        var px = Enumerable.Repeat(target, 100).ToList();
        Assert.Equal(target, RegionColorAnalyzer.Analyze(px).Color);
    }

    [Fact]
    public void Analyzer_ignores_specular_highlights_and_outliers()
    {
        var baseColor = Rgb.FromHex("#C0392B");
        var px = Enumerable.Repeat(baseColor, 80).ToList();
        px.AddRange(Enumerable.Repeat(new Rgb(255, 255, 255), 12)); // glare
        px.AddRange(Enumerable.Repeat(new Rgb(0, 0, 0), 4));        // deep shadow
        px.AddRange(Enumerable.Repeat(new Rgb(20, 200, 40), 4));    // stray green object

        var result = RegionColorAnalyzer.Analyze(px);
        Assert.True(Ciede2000.Difference(baseColor, result.Color) < 1.0);
        Assert.Equal(12, result.DiscardedHighlights);
        Assert.Equal(4, result.DiscardedShadows);
    }

    [Fact]
    public void Analyzer_keeps_genuine_white_target()
    {
        var px = Enumerable.Repeat(new Rgb(250, 250, 250), 50).ToList();
        var result = RegionColorAnalyzer.Analyze(px);
        Assert.True(Ciede2000.Difference(new Rgb(250, 250, 250), result.Color) < 1.0);
    }

    [Fact]
    public void Analyzer_is_deterministic()
    {
        var rnd = new Random(7);
        var px = Enumerable.Range(0, 500).Select(_ => new Rgb((byte)rnd.Next(100, 140), (byte)rnd.Next(60, 90), (byte)rnd.Next(30, 60))).ToList();
        Assert.Equal(RegionColorAnalyzer.Analyze(px).Color, RegionColorAnalyzer.Analyze(px).Color);
    }
}
