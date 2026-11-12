using ColorMatch.Domain.Enums;
using ColorMatch.Domain.Rules;

namespace ColorMatch.Tests;

public class DailyHuntRulesTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);

    [Fact]
    public void The_three_colours_of_a_day_are_deterministic_valid_and_all_different()
    {
        for (var i = 0; i < 400; i++)
        {
            var day = Start.AddDays(i);
            var picks = new[] { HuntDifficulty.Easy, HuntDifficulty.Medium, HuntDifficulty.Hard }.Select(d => DailyHuntPalette.ForDate(day, d)).ToList();

            Assert.All(picks, p => Assert.Matches("^#[0-9A-F]{6}$", p.Hex));
            Assert.Equal(3, picks.Select(p => p.Hex).Distinct().Count());   // never the same colour twice in one day
            Assert.Equal(picks.Select(p => p.Hex), new[] { HuntDifficulty.Easy, HuntDifficulty.Medium, HuntDifficulty.Hard }.Select(d => DailyHuntPalette.ForDate(day, d).Hex));
        }
    }

    [Fact]
    public void The_hunt_pools_are_the_atlas_catalogue_split_by_difficulty()
    {
        foreach (var d in new[] { HuntDifficulty.Easy, HuntDifficulty.Medium, HuntDifficulty.Hard })
            for (var i = 0; i < 80; i++)
                Assert.Equal(d, AtlasCatalog.FindByHex(DailyHuntPalette.ForDate(Start.AddDays(i), d).Hex)!.Difficulty);
    }

    [Fact]
    public void Every_colour_of_each_list_comes_up_before_any_repeats()
    {
        foreach (var (difficulty, size) in new[] { (HuntDifficulty.Easy, DailyHuntPalette.Easy.Count), (HuntDifficulty.Medium, DailyHuntPalette.Medium.Count), (HuntDifficulty.Hard, DailyHuntPalette.Hard.Count) })
        {
            var seen = Enumerable.Range(0, size).Select(i => DailyHuntPalette.ForDate(Start.AddDays(i), difficulty).Hex).Distinct().Count();
            Assert.Equal(size, seen);
        }
    }

    [Fact]
    public void The_palettes_do_not_overlap_so_difficulties_never_collide()
    {
        var easy = DailyHuntPalette.Easy.Select(c => c.Hex).ToHashSet();
        var medium = DailyHuntPalette.Medium.Select(c => c.Hex).ToHashSet();
        var hard = DailyHuntPalette.Hard.Select(c => c.Hex).ToHashSet();
        Assert.Empty(easy.Intersect(medium));
        Assert.Empty(easy.Intersect(hard));
        Assert.Empty(medium.Intersect(hard));
    }

    [Theory]
    [InlineData(1000, ScoreTier.Perfect)]
    [InlineData(950, ScoreTier.Perfect)]
    [InlineData(949, ScoreTier.Excellent)]
    [InlineData(850, ScoreTier.Excellent)]
    [InlineData(849, ScoreTier.Great)]
    [InlineData(700, ScoreTier.Great)]
    [InlineData(699, ScoreTier.Good)]
    [InlineData(500, ScoreTier.Good)]
    [InlineData(499, ScoreTier.NeedsImprovement)]
    [InlineData(0, ScoreTier.NeedsImprovement)]
    public void Score_tiers_follow_the_bands(int score, ScoreTier tier) => Assert.Equal(tier, ScoreTiers.For(score));

    [Fact]
    public void The_balance_defaults_match_the_design()
    {
        var b = new GameBalance().DailyHunt;
        Assert.Equal((100, 200, 350), (b.XpFor(HuntDifficulty.Easy), b.XpFor(HuntDifficulty.Medium), b.XpFor(HuntDifficulty.Hard)));
        Assert.Equal(700, b.MinScore);
        Assert.True(b.CompletionBonusXp > 0);
    }
}
