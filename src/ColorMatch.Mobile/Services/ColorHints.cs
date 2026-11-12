using ColorMatch.ColorEngine.ColorAnalysis;
using ColorMatch.ColorEngine.ColorConversion;

namespace ColorMatch.Mobile.Services;

/// <summary>Turns the engine's <see cref="ColorTip"/>s into a short Turkish sentence the player can act on.</summary>
public static class ColorHints
{
    /// <summary>Above this score the match is already good: no advice, just praise elsewhere.</summary>
    public const int GoodEnough = 850;

    private static string Word(ColorTip tip) => tip switch
    {
        ColorTip.Lighter => "daha açık",
        ColorTip.Darker => "daha koyu",
        ColorTip.MoreVivid => "daha canlı",
        ColorTip.Duller => "daha soluk",
        ColorTip.Redder => "daha kırmızımsı",
        ColorTip.Greener => "daha yeşilimsi",
        ColorTip.Yellower => "daha sarımsı",
        _ => "daha mavimsi",
    };

    /// <summary>"Daha açık ve daha sarımsı bir ton ara." — empty when there is nothing useful to say.</summary>
    public static string Describe(string targetHex, string? foundHex, int score)
    {
        if (score >= GoodEnough || string.IsNullOrEmpty(foundHex)) return "";
        if (!Rgb.TryParseHex(targetHex, out var target) || !Rgb.TryParseHex(foundHex, out var found)) return "";
        var tips = ColorAdvice.For(target, found);
        if (tips.Count == 0) return "";
        var text = string.Join(" ve ", tips.Select(Word));
        return char.ToUpper(text[0], new System.Globalization.CultureInfo("tr-TR")) + text[1..] + " bir ton ara.";
    }
}
