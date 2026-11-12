using ColorMatch.ColorEngine.ColorConversion;

namespace ColorMatch.ColorEngine.ColorAnalysis;

/// <summary>
/// Groups any colour into one of a dozen human-friendly families (used to browse posts "by colour").
/// Classification works in CIELCh: low chroma → neutrals by lightness, otherwise by hue angle; dark oranges are "Kahverengi".
/// </summary>
public static class ColorFamilies
{
    public sealed record Family(string Name, string Hex);

    public static readonly IReadOnlyList<Family> All =
    [
        new("Kırmızı", "#D9392B"), new("Turuncu", "#F07F1A"), new("Sarı", "#F4C542"), new("Yeşil", "#4CAF50"),
        new("Turkuaz", "#2BB5B0"), new("Mavi", "#2A56C6"), new("Mor", "#7E57C2"), new("Pembe", "#EC6FA5"),
        new("Kahverengi", "#7A4E2D"), new("Beyaz", "#F5F5F5"), new("Gri", "#8A8D91"), new("Siyah", "#1B1B1F"),
    ];

    private static readonly HashSet<string> Names = All.Select(f => f.Name).ToHashSet();

    public static bool IsKnown(string? name) => name is not null && Names.Contains(name);

    public static string Classify(Rgb rgb)
    {
        var lab = ColorConverter.ToLab(rgb);
        var chroma = lab.Chroma;

        if (chroma < 9)
            return lab.L > 82 ? "Beyaz" : lab.L < 24 ? "Siyah" : "Gri";

        var hue = Math.Atan2(lab.B, lab.A) * 180.0 / Math.PI;
        if (hue < 0) hue += 360;

        // very dark colours with little chroma are black rather than "dark blue" etc.
        if (lab.L < 14) return "Siyah";

        // dark / muted warm tones read as brown (rust, chocolate, coffee…)
        if (hue is >= 38 and < 95 && lab.L < 58 && chroma < 56) return "Kahverengi";

        // light, soft reds are pinks (dusty rose, salmon-pink…)
        if (hue is >= 10 and < 48 && lab.L > 62 && chroma < 45) return "Pembe";

        // hue boundaries are CIELCh angles (not HSL): red ≈ 30–40°, orange ≈ 60°, yellow ≈ 90°, green ≈ 135°,
        // cyan ≈ 195°, blue ≈ 260–295°, violet ≈ 310°, magenta/pink ≈ 350°.
        return hue switch
        {
            >= 335 or < 10 => "Pembe",
            < 48 => "Kırmızı",
            < 75 => "Turuncu",
            < 105 => "Sarı",
            < 170 => "Yeşil",
            < 225 => "Turkuaz",
            < 298 => "Mavi",
            _ => "Mor",
        };
    }

    public static string? Classify(string? hex) => Rgb.TryParseHex(hex, out var rgb) ? Classify(rgb) : null;
}
