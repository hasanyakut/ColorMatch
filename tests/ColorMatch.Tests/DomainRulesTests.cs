using ColorMatch.Domain.Rules;

namespace ColorMatch.Tests;

public class DomainRulesTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(399, 2)]
    [InlineData(400, 3)]
    [InlineData(900, 4)]
    public void Level_for_xp(int xp, int level) => Assert.Equal(level, LevelRules.LevelForXp(xp));

    [Fact]
    public void Xp_for_score_is_monotonic_and_bounded()
    {
        var prev = -1;
        for (var s = 0; s <= 1000; s++)
        {
            var xp = XpRules.XpForScore(s);
            Assert.True(xp >= prev);
            prev = xp;
        }
        Assert.Equal(0, XpRules.XpForScore(0));
        Assert.Equal(175, XpRules.XpForScore(1000));
    }

    [Theory]
    [InlineData(500, 0, 50)]
    [InlineData(500, 50, 0)]
    [InlineData(400, 50, 0)]   // worse score never takes XP away
    [InlineData(1000, 50, 125)]
    public void Xp_to_award_pays_only_the_improvement(int score, int already, int expected)
        => Assert.Equal(expected, XpRules.XpToAward(score, already));

    [Fact]
    public void Daily_colour_is_deterministic_and_cycles_through_all_colours()
    {
        var d = new DateOnly(2026, 11, 9);
        Assert.Equal(DailyColorPalette.ForDate(d), DailyColorPalette.ForDate(d));
        var distinct = Enumerable.Range(0, DailyColorPalette.Colors.Count)
            .Select(i => DailyColorPalette.ForDate(d.AddDays(i)).Hex).Distinct().Count();
        Assert.Equal(DailyColorPalette.Colors.Count, distinct);
        Assert.NotEqual(DailyColorPalette.ForDate(d), DailyColorPalette.ForDate(d.AddDays(1)));
    }

    [Fact]
    public void Palette_hex_values_are_valid_and_unique()
    {
        foreach (var c in DailyColorPalette.Colors)
            Assert.True(ColorMatch.ColorEngine.ColorConversion.Rgb.TryParseHex(c.Hex, out _), c.Hex);
        Assert.Equal(DailyColorPalette.Colors.Count, DailyColorPalette.Colors.Select(c => c.Hex).Distinct().Count());
    }
}

public class StreakRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    [Fact] public void No_plays_is_zero() => Assert.Equal(0, StreakRules.Compute([], Today));
    [Fact] public void Played_today_only_is_one() => Assert.Equal(1, StreakRules.Compute([Today], Today));
    [Fact] public void Consecutive_days_count() => Assert.Equal(3, StreakRules.Compute([Today, Today.AddDays(-1), Today.AddDays(-2)], Today));
    [Fact] public void Streak_survives_until_the_day_ends() => Assert.Equal(2, StreakRules.Compute([Today.AddDays(-1), Today.AddDays(-2)], Today));
    [Fact] public void Gap_breaks_the_streak() => Assert.Equal(1, StreakRules.Compute([Today, Today.AddDays(-2)], Today));
    [Fact] public void Missing_yesterday_and_today_is_zero() => Assert.Equal(0, StreakRules.Compute([Today.AddDays(-2)], Today));
}
