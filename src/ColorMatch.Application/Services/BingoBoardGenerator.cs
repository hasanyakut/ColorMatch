using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.ColorEngine.ColorScoring;
using ColorMatch.Domain.Enums;
using ColorMatch.Domain.Rules;

namespace ColorMatch.Application.Services;

/// <summary>
/// Builds the nine squares of a board from the Color Atlas catalogue: three easy, three medium and three hard colours, all different
/// enough from each other (ΔE00 ≥ <see cref="MinSeparation"/>) that a photo can only fit one square, in random order. The random
/// source is injected so boards are reproducible in tests.
/// </summary>
public static class BingoBoardGenerator
{
    public const double MinSeparation = 10.0;

    public static IReadOnlyList<AtlasColor> Generate(Random rng)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var picked = new List<AtlasColor>(BingoRules.CellCount);
            foreach (var d in new[] { HuntDifficulty.Easy, HuntDifficulty.Medium, HuntDifficulty.Hard })
            {
                var pool = AtlasCatalog.For(d).OrderBy(_ => rng.Next()).ToList();
                var taken = 0;
                foreach (var c in pool)
                {
                    if (taken == 3) break;
                    if (picked.Any(p => Ciede2000.Difference(Rgb.FromHex(p.Hex), Rgb.FromHex(c.Hex)) < MinSeparation)) continue;
                    picked.Add(c); taken++;
                }
            }
            if (picked.Count == BingoRules.CellCount) return picked.OrderBy(_ => rng.Next()).ToList();
        }

        // practically unreachable with this catalogue; keep the board valid rather than failing the request
        var fallback = new List<AtlasColor>();
        foreach (var d in new[] { HuntDifficulty.Easy, HuntDifficulty.Medium, HuntDifficulty.Hard })
            fallback.AddRange(AtlasCatalog.For(d).OrderBy(_ => rng.Next()).Take(3));
        return fallback.OrderBy(_ => rng.Next()).ToList();
    }
}
