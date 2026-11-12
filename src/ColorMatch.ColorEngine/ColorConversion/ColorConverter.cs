namespace ColorMatch.ColorEngine.ColorConversion;

/// <summary>sRGB &lt;-&gt; CIE XYZ (D65) &lt;-&gt; CIE L*a*b* conversions.</summary>
public static class ColorConverter
{
    // D65 reference white (2° observer), Y = 1.
    private const double Xn = 0.95047;
    private const double Yn = 1.00000;
    private const double Zn = 1.08883;

    private const double Epsilon = 216.0 / 24389.0; // (6/29)^3
    private const double Kappa = 24389.0 / 27.0;

    // 8-bit sRGB -> linear lookup (exact IEC 61966-2-1 transfer function).
    private static readonly double[] LinearTable = BuildLinearTable();

    private static double[] BuildLinearTable()
    {
        var t = new double[256];
        for (var i = 0; i < 256; i++)
        {
            var c = i / 255.0;
            t[i] = c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return t;
    }

    public static Lab ToLab(Rgb rgb)
    {
        var r = LinearTable[rgb.R];
        var g = LinearTable[rgb.G];
        var b = LinearTable[rgb.B];

        var x = 0.4124564 * r + 0.3575761 * g + 0.1804375 * b;
        var y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
        var z = 0.0193339 * r + 0.1191920 * g + 0.9503041 * b;

        var fx = F(x / Xn);
        var fy = F(y / Yn);
        var fz = F(z / Zn);

        return new Lab(116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
    }

    public static Rgb ToRgb(Lab lab)
    {
        var fy = (lab.L + 16.0) / 116.0;
        var fx = fy + lab.A / 500.0;
        var fz = fy - lab.B / 200.0;

        var x = Xn * FInv(fx);
        var y = Yn * (lab.L > Kappa * Epsilon ? Math.Pow(fy, 3) : lab.L / Kappa);
        var z = Zn * FInv(fz);

        var r = 3.2404542 * x - 1.5371385 * y - 0.4985314 * z;
        var g = -0.9692660 * x + 1.8760108 * y + 0.0415560 * z;
        var b = 0.0556434 * x - 0.2040259 * y + 1.0572252 * z;

        return new Rgb(Encode(r), Encode(g), Encode(b));
    }

    private static double F(double t) =>
        t > Epsilon ? Math.Cbrt(t) : (Kappa * t + 16.0) / 116.0;

    private static double FInv(double f)
    {
        var f3 = f * f * f;
        return f3 > Epsilon ? f3 : (116.0 * f - 16.0) / Kappa;
    }

    private static byte Encode(double linear)
    {
        linear = Math.Clamp(linear, 0.0, 1.0);
        var c = linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
        return (byte)Math.Clamp((int)Math.Round(c * 255.0), 0, 255);
    }
}
