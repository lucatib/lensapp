namespace LensApp.Models;

/// <summary>sRGB / CIE Lab conversions and the CIEDE2000 colour difference.</summary>
public static class ColorMath
{
    // D65 reference white, 2° observer.
    const double Xn = 95.047, Yn = 100.000, Zn = 108.883;

    static double SrgbToLinear(double c)
    {
        c /= 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>Lookup table for the 8-bit sRGB -> linear-light transfer function.</summary>
    static readonly double[] LinearLut = BuildLinearLut();

    static double[] BuildLinearLut()
    {
        var lut = new double[256];
        for (var i = 0; i < 256; i++) lut[i] = SrgbToLinear(i);
        return lut;
    }

    /// <summary>8-bit sRGB channel value to linear light (0..1).</summary>
    public static double ToLinear(byte c) => LinearLut[c];

    /// <summary>Linear light (0..1) back to an 8-bit sRGB channel value.</summary>
    public static byte FromLinear(double lin)
    {
        if (lin <= 0.0) return 0;
        if (lin >= 1.0) return 255;
        var s = lin <= 0.0031308 ? lin * 12.92 : 1.055 * Math.Pow(lin, 1.0 / 2.4) - 0.055;
        return Clamp8(s * 255.0);
    }

    public static (double L, double A, double B) RgbToLab(byte r, byte g, byte b)
    {
        var rl = SrgbToLinear(r) * 100.0;
        var gl = SrgbToLinear(g) * 100.0;
        var bl = SrgbToLinear(b) * 100.0;

        var x = rl * 0.4124564 + gl * 0.3575761 + bl * 0.1804375;
        var y = rl * 0.2126729 + gl * 0.7151522 + bl * 0.0721750;
        var z = rl * 0.0193339 + gl * 0.1191920 + bl * 0.9503041;

        static double F(double t) => t > 0.008856451679 ? Math.Cbrt(t) : (903.2962962 * t + 16.0) / 116.0;

        var fx = F(x / Xn);
        var fy = F(y / Yn);
        var fz = F(z / Zn);

        return (116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
    }

    /// <summary>Relative luminance (0..1) of an sRGB triplet.</summary>
    public static double Luminance(byte r, byte g, byte b) =>
        0.2126 * SrgbToLinear(r) + 0.7152 * SrgbToLinear(g) + 0.0722 * SrgbToLinear(b);

    /// <summary>CIEDE2000 difference between two Lab colours (kL = kC = kH = 1).</summary>
    public static double DeltaE2000(
        double l1, double a1, double b1,
        double l2, double a2, double b2)
    {
        const double deg2Rad = Math.PI / 180.0;
        const double rad2Deg = 180.0 / Math.PI;

        var c1 = Math.Sqrt(a1 * a1 + b1 * b1);
        var c2 = Math.Sqrt(a2 * a2 + b2 * b2);
        var cBar = (c1 + c2) / 2.0;

        var cBar7 = Math.Pow(cBar, 7);
        var g = 0.5 * (1.0 - Math.Sqrt(cBar7 / (cBar7 + 6103515625.0))); // 25^7

        var a1p = (1.0 + g) * a1;
        var a2p = (1.0 + g) * a2;

        var c1p = Math.Sqrt(a1p * a1p + b1 * b1);
        var c2p = Math.Sqrt(a2p * a2p + b2 * b2);

        var h1p = HueAngle(b1, a1p);
        var h2p = HueAngle(b2, a2p);

        var dLp = l2 - l1;
        var dCp = c2p - c1p;

        double dhp;
        if (c1p * c2p == 0.0)
        {
            dhp = 0.0;
        }
        else
        {
            var diff = h2p - h1p;
            dhp = diff switch
            {
                > 180.0 => diff - 360.0,
                < -180.0 => diff + 360.0,
                _ => diff,
            };
        }

        var dHp = 2.0 * Math.Sqrt(c1p * c2p) * Math.Sin(dhp / 2.0 * deg2Rad);

        var lBarp = (l1 + l2) / 2.0;
        var cBarp = (c1p + c2p) / 2.0;

        double hBarp;
        if (c1p * c2p == 0.0)
        {
            hBarp = h1p + h2p;
        }
        else
        {
            var diff = Math.Abs(h1p - h2p);
            var sum = h1p + h2p;
            if (diff <= 180.0) hBarp = sum / 2.0;
            else if (sum < 360.0) hBarp = (sum + 360.0) / 2.0;
            else hBarp = (sum - 360.0) / 2.0;
        }

        var t = 1.0
                - 0.17 * Math.Cos((hBarp - 30.0) * deg2Rad)
                + 0.24 * Math.Cos(2.0 * hBarp * deg2Rad)
                + 0.32 * Math.Cos((3.0 * hBarp + 6.0) * deg2Rad)
                - 0.20 * Math.Cos((4.0 * hBarp - 63.0) * deg2Rad);

        var dTheta = 30.0 * Math.Exp(-Math.Pow((hBarp - 275.0) / 25.0, 2));

        var cBarp7 = Math.Pow(cBarp, 7);
        var rc = 2.0 * Math.Sqrt(cBarp7 / (cBarp7 + 6103515625.0));

        var lBarpMinus50Sq = (lBarp - 50.0) * (lBarp - 50.0);
        var sl = 1.0 + 0.015 * lBarpMinus50Sq / Math.Sqrt(20.0 + lBarpMinus50Sq);
        var sc = 1.0 + 0.045 * cBarp;
        var sh = 1.0 + 0.015 * cBarp * t;
        var rt = -Math.Sin(2.0 * dTheta * deg2Rad) * rc;

        var termL = dLp / sl;
        var termC = dCp / sc;
        var termH = dHp / sh;

        return Math.Sqrt(termL * termL + termC * termC + termH * termH + rt * termC * termH);

        static double HueAngle(double b, double ap)
        {
            if (ap == 0.0 && b == 0.0) return 0.0;
            var h = Math.Atan2(b, ap) * rad2Deg;
            return h < 0.0 ? h + 360.0 : h;
        }
    }

    public static double DeltaE2000(in (double L, double A, double B) x, RalColor ral) =>
        DeltaE2000(x.L, x.A, x.B, ral.L, ral.A, ral.Bb);

    public static byte Clamp8(double v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)Math.Round(v);
}
