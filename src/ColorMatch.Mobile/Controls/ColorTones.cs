using ColorMatch.ColorEngine.ColorConversion;

namespace ColorMatch.Mobile.Controls;

/// <summary>One step of a colour's tonal ladder (lighter ↔ darker, same hue). <see cref="IsBase"/> marks the colour itself.</summary>
public sealed record ToneItem(string Hex, bool IsBase);

public static class ColorTones
{
    /// <summary>
    /// Five tones of <paramref name="hex"/> by moving CIELAB lightness (−24, −12, 0, +12, +24).
    /// Useful when hunting: the real object is rarely the exact tone, so the ladder shows what "a bit lighter / darker" looks like.
    /// </summary>
    public static IReadOnlyList<ToneItem> For(string? hex)
    {
        if (!Rgb.TryParseHex(hex, out var rgb)) return [];
        var lab = ColorConverter.ToLab(rgb);
        var result = new List<ToneItem>();
        foreach (var delta in new[] { -24.0, -12.0, 0.0, 12.0, 24.0 })
        {
            var l = Math.Clamp(lab.L + delta, 4, 97);
            var tone = delta == 0 ? rgb : ColorConverter.ToRgb(new Lab(l, lab.A, lab.B));
            result.Add(new ToneItem(tone.ToHex(), delta == 0));
        }
        return result;
    }
}
