using System.Globalization;

namespace TMConcept.Core.References;

/// <summary>sRGB ↔ CIELAB (D65, L 0–100) for the reference's colour measure (ReelMimic <c>look.py</c> measures colour in CIELAB too). Pure.</summary>
public static class LabColor
{
    private const double Xn = 0.95047;
    private const double Yn = 1.0;
    private const double Zn = 1.08883;
    private static readonly double[] Linear = [.. Enumerable.Range(0, 256).Select(v => ToLinear(v / 255.0))];

    /// <summary>The CIELAB of an sRGB colour (0–255 each).</summary>
    public static (double L, double A, double B) FromRgb(byte r, byte g, byte b)
    {
        var (lr, lg, lb) = (Linear[r], Linear[g], Linear[b]);
        var x = ((0.4124564 * lr) + (0.3575761 * lg) + (0.1804375 * lb)) / Xn;
        var y = ((0.2126729 * lr) + (0.7151522 * lg) + (0.0721750 * lb)) / Yn;
        var z = ((0.0193339 * lr) + (0.1191920 * lg) + (0.9503041 * lb)) / Zn;
        var (fx, fy, fz) = (F(x), F(y), F(z));
        return ((116 * fy) - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    /// <summary>The #RRGGBB of a CIELAB colour (out-of-gamut values are clipped).</summary>
    public static string ToHex(double l, double a, double b)
    {
        var fy = (l + 16) / 116;
        var fx = fy + (a / 500);
        var fz = fy - (b / 200);
        var x = Xn * FInverse(fx);
        var y = Yn * FInverse(fy);
        var z = Zn * FInverse(fz);
        var r = (3.2404542 * x) - (1.5371385 * y) - (0.4985314 * z);
        var g = (-0.9692660 * x) + (1.8760108 * y) + (0.0415560 * z);
        var bl = (0.0556434 * x) - (0.2040259 * y) + (1.0572252 * z);
        return string.Create(CultureInfo.InvariantCulture, $"#{Byte(r):X2}{Byte(g):X2}{Byte(bl):X2}");
    }

    private static int Byte(double linear) => (int)Math.Round(Math.Clamp(ToGamma(Math.Clamp(linear, 0, 1)), 0, 1) * 255);

    private static double ToLinear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    private static double ToGamma(double v) => v <= 0.0031308 ? v * 12.92 : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;

    private static double F(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : ((24389.0 / 27 * t) + 16) / 116;

    private static double FInverse(double t) => t * t * t > 216.0 / 24389 ? t * t * t : ((116 * t) - 16) / (24389.0 / 27);
}
