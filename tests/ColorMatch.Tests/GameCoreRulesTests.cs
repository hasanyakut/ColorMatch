using ColorMatch.Domain.Rules;

namespace ColorMatch.Tests;

public class LevelCurveTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(399, 2)]
    [InlineData(400, 3)]
    [InlineData(900, 4)]
    [InlineData(10_000, 11)]
    public void Default_curve_matches_the_original_quadratic_levels(int xp, int level)
        => Assert.Equal(level, LevelCurve.Default.LevelForXp(xp));

    [Fact]
    public void Level_and_xp_thresholds_agree_for_every_level()
    {
        var c = LevelCurve.Default;
        for (var level = 1; level <= 100; level++)
        {
            var start = c.XpForLevel(level);
            Assert.Equal(level, c.LevelForXp(start));
            if (level > 1) Assert.Equal(level - 1, c.LevelForXp(start - 1));
        }
    }

    [Fact]
    public void Every_next_level_costs_more_than_the_one_before()
    {
        var c = LevelCurve.Default;
        var prev = 0;
        for (var level = 1; level < 60; level++)
        {
            var cost = c.XpForLevel(level + 1) - c.XpForLevel(level);
            Assert.True(cost > prev, $"level {level}→{level + 1} must cost more than the previous step");
            prev = cost;
        }
    }

    [Fact]
    public void The_curve_follows_the_configured_balance()
    {
        var c = new LevelCurve(new GameBalance { LevelBaseXp = 200, LevelExponent = 1.5 });
        Assert.Equal(0, c.XpForLevel(1));
        Assert.Equal(200, c.XpForLevel(2));
        Assert.Equal(566, c.XpForLevel(3));   // 200 · 2^1.5
        Assert.Equal(2, c.LevelForXp(565 - 365));
        Assert.Equal(3, c.LevelForXp(566));
    }

    [Theory]
    [InlineData(1, "Color Beginner")]
    [InlineData(4, "Color Beginner")]
    [InlineData(5, "Color Seeker")]
    [InlineData(9, "Color Seeker")]
    [InlineData(10, "Color Explorer")]
    [InlineData(19, "Color Explorer")]
    [InlineData(20, "Color Hunter")]
    [InlineData(34, "Color Hunter")]
    [InlineData(35, "Color Master")]
    [InlineData(80, "Color Master")]
    public void Default_titles_follow_the_level_ranges(int level, string title)
        => Assert.Equal(title, LevelCurve.Default.TitleForLevel(level));

    [Fact]
    public void Titles_are_configurable()
    {
        var c = new LevelCurve(new GameBalance { Titles = [new(1, "Rookie"), new(3, "Pro")] });
        Assert.Equal("Rookie", c.TitleForLevel(2));
        Assert.Equal("Pro", c.TitleForLevel(3));
    }
}

public class StreakStateTests
{
    private static readonly DateOnly D = new(2026, 3, 10);

    [Fact]
    public void First_activity_starts_the_streak_at_one()
    {
        var s = StreakRules.Advance(default, D);
        Assert.Equal(new StreakState(1, 1, D), s);
    }

    [Fact]
    public void A_second_activity_on_the_same_day_does_not_extend_the_streak()
    {
        var s = StreakRules.Advance(StreakRules.Advance(default, D), D);
        Assert.Equal(1, s.Current);
    }

    [Fact]
    public void Consecutive_days_extend_the_streak_and_track_the_longest()
    {
        var s = default(StreakState);
        for (var i = 0; i < 5; i++) s = StreakRules.Advance(s, D.AddDays(i));
        Assert.Equal(5, s.Current);
        Assert.Equal(5, s.Longest);
        Assert.Equal(D.AddDays(4), s.Last);
    }

    [Fact]
    public void A_missed_day_resets_the_streak_to_one_but_keeps_the_longest()
    {
        var s = default(StreakState);
        for (var i = 0; i < 4; i++) s = StreakRules.Advance(s, D.AddDays(i));   // 4 days
        s = StreakRules.Advance(s, D.AddDays(6));                              // missed 2 days
        Assert.Equal(1, s.Current);
        Assert.Equal(4, s.Longest);
    }

    [Fact]
    public void A_clock_that_goes_backwards_changes_nothing()
    {
        var s = StreakRules.Advance(StreakRules.Advance(default, D), D.AddDays(1));
        Assert.Equal(s, StreakRules.Advance(s, D));
    }

    [Fact]
    public void Effective_streak_survives_until_the_end_of_the_day_after_the_last_activity()
    {
        var s = new StreakState(4, 6, D);
        Assert.Equal(4, StreakRules.Effective(s, D));             // played today
        Assert.Equal(4, StreakRules.Effective(s, D.AddDays(1)));  // not yet today: still alive
        Assert.Equal(0, StreakRules.Effective(s, D.AddDays(2)));  // a full day missed
        Assert.Equal(0, StreakRules.Effective(default, D));
    }

    [Fact]
    public void History_rebuild_finds_current_and_longest_runs()
    {
        var days = new[] { D, D.AddDays(1), D.AddDays(2), D.AddDays(3), D.AddDays(10), D.AddDays(11), D.AddDays(11) };
        var s = StreakRules.FromHistory(days);
        Assert.Equal(new StreakState(2, 4, D.AddDays(11)), s);
        Assert.Equal(default, StreakRules.FromHistory([]));
    }
}
