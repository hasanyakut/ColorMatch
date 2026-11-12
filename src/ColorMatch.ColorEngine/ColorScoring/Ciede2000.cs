using ColorMatch.ColorEngine.ColorConversion;

namespace ColorMatch.ColorEngine.ColorScoring;

/// <summary>
/// CIEDE2000 colour difference (Sharma, Wu &amp; Dalal 2005 formulation), kL = kC = kH = 1.
/// </summary>
public static class Ciede2000
{
    public static double Difference(Rgb a, Rgb b) =>
        Difference(ColorConverter.ToLab(a), ColorConverter.ToLab(b));

    public static double Difference(Lab c1, Lab c2)
    {
        const double kL = 1, kC = 1, kH = 1;
        const double pow25_7 = 6103515625.0; // 25^7

        var cab1 = Math.Sqrt(c1.A * c1.A + c1.B * c1.B);
        var cab2 = Math.Sqrt(c2.A * c2.A + c2.B * c2.B);
        var cabMean = (cab1 + cab2) / 2.0;
        var cabMean7 = Math.Pow(cabMean, 7);
        var g = 0.5 * (1.0 - Math.Sqrt(cabMean7 / (cabMean7 + pow25_7)));

        var a1p = (1.0 + g) * c1.A;
        var a2p = (1.0 + g) * c2.A;
        var c1p = Math.Sqrt(a1p * a1p + c1.B * c1.B);
        var c2p = Math.Sqrt(a2p * a2p + c2.B * c2.B);
        var h1p = HueDegrees(c1.B, a1p);
        var h2p = HueDegrees(c2.B, a2p);

        var dLp = c2.L - c1.L;
        var dCp = c2p - c1p;

        double dhp;
        if (c1p * c2p == 0) dhp = 0;
        else
        {
            var d = h2p - h1p;
            if (d > 180) d -= 360;
            else if (d < -180) d += 360;
            dhp = d;
        }
        var dHp = 2.0 * Math.Sqrt(c1p * c2p) * Math.Sin(ToRad(dhp / 2.0));

        var lpMean = (c1.L + c2.L) / 2.0;
        var cpMean = (c1p + c2p) / 2.0;

        double hpMean;
        if (c1p * c2p == 0) hpMean = h1p + h2p;
        else if (Math.Abs(h1p - h2p) <= 180) hpMean = (h1p + h2p) / 2.0;
        else if (h1p + h2p < 360) hpMean = (h1p + h2p + 360) / 2.0;
        else hpMean = (h1p + h2p - 360) / 2.0;

        var t = 1.0
                - 0.17 * Math.Cos(ToRad(hpMean - 30))
                + 0.24 * Math.Cos(ToRad(2 * hpMean))
                + 0.32 * Math.Cos(ToRad(3 * hpMean + 6))
                - 0.20 * Math.Cos(ToRad(4 * hpMean - 63));

        var dTheta = 30.0 * Math.Exp(-Math.Pow((hpMean - 275.0) / 25.0, 2));
        var cpMean7 = Math.Pow(cpMean, 7);
        var rc = 2.0 * Math.Sqrt(cpMean7 / (cpMean7 + pow25_7));
        var lpm50sq = (lpMean - 50.0) * (lpMean - 50.0);
        var sl = 1.0 + 0.015 * lpm50sq / Math.Sqrt(20.0 + lpm50sq);
        var sc = 1.0 + 0.045 * cpMean;
        var sh = 1.0 + 0.015 * cpMean * t;
        var rt = -Math.Sin(ToRad(2.0 * dTheta)) * rc;

        var l = dLp / (kL * sl);
        var c = dCp / (kC * sc);
        var h = dHp / (kH * sh);

        return Math.Sqrt(l * l + c * c + h * h + rt * c * h);
    }

    private static double HueDegrees(double b, double ap)
    {
        if (b == 0 && ap == 0) return 0;
        var h = Math.Atan2(b, ap) * 180.0 / Math.PI;
        return h < 0 ? h + 360.0 : h;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
