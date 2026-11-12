using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.Domain.Enums;
using ColorMatch.Domain.Rules;

namespace ColorMatch.Tests;

public class AtlasRulesTests
{
    [Fact]
    public void The_atlas_has_five_collections_of_twenty_colours()
    {
        Assert.Equal(new[] { "forest", "ocean", "sunset", "desert", "neon" }, AtlasCatalog.Collections.Select(c => c.Key));
        Assert.All(AtlasCatalog.Collections, c => Assert.Equal(20, c.Colors.Count));
        Assert.Equal(100, AtlasCatalog.All.Count);
    }

    [Fact]
    public void Keys_names_and_hex_values_are_unique_and_valid()
    {
        Assert.Equal(100, AtlasCatalog.All.Select(c => c.Key).Distinct().Count());
        Assert.Equal(100, AtlasCatalog.All.Select(c => c.Name).Distinct().Count());
        Assert.Equal(100, AtlasCatalog.All.Select(c => c.Hex).Distinct().Count());   // a hunt target maps to exactly one colour
        Assert.All(AtlasCatalog.All, c =>
        {
            Assert.Matches("^#[0-9A-F]{6}$", c.Hex);
            Assert.True(Rgb.TryParseHex(c.Hex, out _));
            Assert.Equal(c, AtlasCatalog.FindByHex(c.Hex.ToLowerInvariant()));
            Assert.Equal(c, AtlasCatalog.FindByKey(c.Key));
        });
        Assert.Null(AtlasCatalog.FindByHex("#010203"));
        Assert.Null(AtlasCatalog.FindByKey("nope"));
    }

    [Fact]
    public void Colours_are_distinct_enough_to_be_told_apart()
    {
        // two colours closer than ΔE00 3 would be indistinguishable for a player
        var all = AtlasCatalog.All;
        var tooClose = new List<string>();
        for (var i = 0; i < all.Count; i++)
            for (var j = i + 1; j < all.Count; j++)
            {
                var de = ColorMatch.ColorEngine.ColorScoring.Ciede2000.Difference(Rgb.FromHex(all[i].Hex), Rgb.FromHex(all[j].Hex));
                if (de < 3.0) tooClose.Add($"{all[i].Name} ~ {all[j].Name} ({de:0.0})");
            }
        Assert.True(tooClose.Count == 0, string.Join("; ", tooClose));
    }

    [Fact]
    public void Difficulty_pools_are_sized_for_coprime_steps_so_every_colour_comes_up_each_cycle()
    {
        Assert.Equal(34, AtlasCatalog.For(HuntDifficulty.Easy).Count);
        Assert.Equal(33, AtlasCatalog.For(HuntDifficulty.Medium).Count);
        Assert.Equal(33, AtlasCatalog.For(HuntDifficulty.Hard).Count);

        var start = new DateOnly(2026, 1, 1);
        foreach (var d in new[] { HuntDifficulty.Easy, HuntDifficulty.Medium, HuntDifficulty.Hard })
        {
            var size = AtlasCatalog.For(d).Count;
            var seen = Enumerable.Range(0, size).Select(i => DailyHuntPalette.ForDate(start.AddDays(i), d).Hex).ToHashSet();
            Assert.Equal(size, seen.Count);
            Assert.All(seen, hex => Assert.Equal(d, AtlasCatalog.FindByHex(hex)!.Difficulty));
        }
    }

    [Fact]
    public void Every_hunt_colour_ever_chosen_is_an_atlas_colour()
    {
        var start = new DateOnly(2026, 1, 1);
        for (var i = 0; i < 400; i++)
            foreach (var d in new[] { HuntDifficulty.Easy, HuntDifficulty.Medium, HuntDifficulty.Hard })
                Assert.NotNull(AtlasCatalog.FindByHex(DailyHuntPalette.ForDate(start.AddDays(i), d).Hex));
    }

    [Fact]
    public void Collection_reward_balance_has_a_default()
        => Assert.Equal(500, new GameBalance().Atlas.CollectionCompleteXp);
}
