namespace ColorMatch.ColorEngine.ColorConversion;

/// <summary>8-bit sRGB colour.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public static bool TryParseHex(string? hex, out Rgb rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6) return false;
        if (!byte.TryParse(s.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)) return false;
        if (!byte.TryParse(s.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)) return false;
        if (!byte.TryParse(s.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b)) return false;
        rgb = new Rgb(r, g, b);
        return true;
    }

    public static Rgb FromHex(string hex) =>
        TryParseHex(hex, out var rgb) ? rgb : throw new FormatException($"Invalid HEX colour '{hex}'.");
}

/// <summary>CIE L*a*b* colour (D65 reference white).</summary>
public readonly record struct Lab(double L, double A, double B)
{
    public double Chroma => Math.Sqrt(A * A + B * B);
}
