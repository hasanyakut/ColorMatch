using ColorMatch.ColorEngine.ColorAnalysis;
using ColorMatch.ColorEngine.ColorConversion;

namespace ColorMatch.Tests.ColorEngine;

public class ColorAdviceTests
{
    private static IReadOnlyList<ColorTip> Tips(string target, string found) => ColorAdvice.For(Rgb.FromHex(target), Rgb.FromHex(found));

    [Fact]
    public void Identical_and_nearly_identical_colours_need_no_advice()
    {
        Assert.Empty(Tips("#2E9E4F", "#2E9E4F"));
        Assert.Empty(Tips("#2E9E4F", "#309F51"));
    }

    [Fact]
    public void A_too_dark_find_is_told_to_go_lighter_and_a_too_light_one_darker()
    {
        Assert.Equal(ColorTip.Lighter, Tips("#8FB7E8", "#4F77A8")[0]);
        Assert.Equal(ColorTip.Darker, Tips("#4F77A8", "#8FB7E8")[0]);
    }

    [Fact]
    public void A_washed_out_find_is_told_to_be_more_vivid_and_a_garish_one_duller()
    {
        // same lightness and hue family, different saturation
        Assert.Contains(ColorTip.MoreVivid, Tips("#D62839", "#A86A70"));
        Assert.Contains(ColorTip.Duller, Tips("#A86A70", "#D62839"));
    }

    [Theory]
    [InlineData("#C8A030", "#A0A040", ColorTip.Redder)]     // found is too green
    [InlineData("#A0A040", "#C8A030", ColorTip.Greener)]
    [InlineData("#9A9A60", "#9A9AA8", ColorTip.Yellower)]   // found is too blue
    [InlineData("#9A9AA8", "#9A9A60", ColorTip.Bluer)]
    public void The_hue_direction_names_what_the_target_has_more_of(string target, string found, ColorTip expected)
        => Assert.Contains(expected, Tips(target, found));

    [Fact]
    public void At_most_two_tips_are_given_and_opposites_never_appear_together()
    {
        var rng = new Random(11);
        for (var i = 0; i < 400; i++)
        {
            var a = new Rgb((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
            var b = new Rgb((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
            var tips = ColorAdvice.For(a, b);
            Assert.InRange(tips.Count, 0, ColorAdvice.MaxTips);
            Assert.Equal(tips.Count, tips.Distinct().Count());
            Assert.False(tips.Contains(ColorTip.Lighter) && tips.Contains(ColorTip.Darker));
            Assert.False(tips.Contains(ColorTip.MoreVivid) && tips.Contains(ColorTip.Duller));
        }
    }

    [Fact]
    public void Advice_is_mirrored_when_target_and_found_swap()
    {
        var forward = Tips("#8FB7E8", "#4F77A8");
        var back = Tips("#4F77A8", "#8FB7E8");
        Assert.Contains(ColorTip.Lighter, forward);
        Assert.Contains(ColorTip.Darker, back);
    }
}
