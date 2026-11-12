using ColorMatch.ColorEngine.ColorAnalysis;
using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.ColorEngine.ColorScoring;

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

    /// <summary>
    /// "Daha açık ve daha sarımsı bir ton ara." and, when the colour covers little of the picture, "Bu renk görselde küçük bir yer tutuyor: nesneye yaklaş."
    /// Empty when there is nothing useful to say.
    /// </summary>
    public static string Describe(string targetHex, string? foundHex, int score, double share = 1)
    {
        if (score >= GoodEnough || string.IsNullOrEmpty(foundHex)) return "";
        if (!Rgb.TryParseHex(targetHex, out var target) || !Rgb.TryParseHex(foundHex, out var found)) return "";
        var parts = new List<string>();
        var tips = ColorAdvice.For(target, found);
        if (tips.Count > 0)
        {
            var text = string.Join(" ve ", tips.Select(Word));
            parts.Add(char.ToUpper(text[0], new System.Globalization.CultureInfo("tr-TR")) + text[1..] + " bir ton ara.");
        }
        // the colour is right but fills too little of the picture: the weight, not the shade, is what costs points
        if (share < ColorWeight.FullCreditShare * 0.7) parts.Add("Bu renk görselde küçük bir yer tutuyor: nesneye yaklaş ya da yakınlaştır.");
        return string.Join(" ", parts);
    }
}
