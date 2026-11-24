using System.Globalization;
using TMConcept.Core.Styles;

namespace TMConcept.Core.Render;

/// <summary>
/// Makes the pixel layer of a video drawn in pixel art (F13, 0.1.51, <see cref="RenderPixel"/>): the buffer's size by the frame's
/// orientation and a colour ramp for every palette colour. A ramp walks two steps darker and two lighter from the palette's colour; the
/// darker steps drift toward blue-violet and gain a little saturation, the lighter ones drift toward warm yellow and lose some (the ramp
/// rule of pixel art, as in ReelMimic's pixel-art engine, MIT; the steps and their numbers here are TM Concept's own). No model is asked and
/// the same palette always gives the same ramps.
/// </summary>
public static class RenderPixelBuilder
{
    /// <summary>The palette tokens that get a ramp, in the order <c>plan.js</c> reads them.</summary>
    public static readonly IReadOnlyList<string> Tokens = ["background", "surface", "accent", "accent2", "text", "muted"];

    private const double LightStep = 0.13;

    private const double HueStep = 7;

    private const double DarkHue = 250;

    private const double LightHue = 50;

    /// <summary>
    /// The layer for <paramref name="style"/> on a <paramref name="width"/> × <paramref name="height"/> frame; <c>null</c> for every style but
    /// <see cref="DrawStyle.PixelArt"/> (whatever its finishing: pixel art has none).
    /// </summary>
    public static RenderPixel? For(VideoStyle? style, RenderColors colors, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (style is not { DrawStyle: DrawStyle.PixelArt })
        {
            return null;
        }

        var upright = height > width;
        var values = new[] { colors.Background, colors.Surface, colors.Accent, colors.Accent2, colors.Text, colors.Muted };
        var ramps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var i = 0; i < Tokens.Count; i++)
        {
            ramps[Tokens[i]] = Ramp(values[i]);
        }

        return new RenderPixel
        {
            Width = upright ? PixelArtLimits.ShortSide : PixelArtLimits.LongSide,
            Height = upright ? PixelArtLimits.LongSide : PixelArtLimits.ShortSide,
            Steps = PixelArtLimits.Steps,
            Block = PixelArtLimits.BlockSize,
            Ramps = ramps,
        };
    }

    /// <summary>
    /// The ramp of <paramref name="hex"/>, dark to light (<see cref="PixelArtLimits.RampLength"/> colours, the given colour in the middle as
    /// #RRGGBB); a value that is not a colour reads as mid grey.
    /// </summary>
    public static List<string> Ramp(string? hex)
    {
        var color = PaletteColors.TryParse(hex, out var parsed) ? parsed : new ColorValue(128, 128, 128, 1);
        var (h, s, l) = Hsl(color);
        var half = PixelArtLimits.RampLength / 2;
        var ramp = new List<string>(PixelArtLimits.RampLength);
        for (var k = -half; k <= half; k++)
        {
            if (k == 0)
            {
                ramp.Add(Hex(color.R, color.G, color.B));
                continue;
            }

            var hue = Toward(h, k < 0 ? DarkHue : LightHue, HueStep * Math.Abs(k));
            var saturation = Math.Clamp(s + (k < 0 ? 0.04 * -k : -0.06 * k), 0, 1);
            var moved = Math.Clamp(l + (LightStep * k), 0.03, 0.97);
            var lightness = k < 0 ? Math.Min(moved, l) : Math.Max(moved, l);
            ramp.Add(FromHsl(hue, saturation, lightness));
        }

        return ramp;
    }

    /// <summary>Moves hue <paramref name="h"/> by at most <paramref name="degrees"/> toward <paramref name="target"/> the short way round.</summary>
    private static double Toward(double h, double target, double degrees)
    {
        var delta = ((target - h + 540) % 360) - 180;
        var step = Math.Clamp(delta, -degrees, degrees);
        return (h + step + 360) % 360;
    }

    private static (double H, double S, double L) Hsl(ColorValue c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-9)
        {
            return (0, 0, l);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r)
        {
            h = ((g - b) / d) + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            h = ((b - r) / d) + 2;
        }
        else
        {
            h = ((r - g) / d) + 4;
        }

        return (h * 60, s, l);
    }

    private static string FromHsl(double h, double s, double l)
    {
        var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        var p = (2 * l) - q;
        var k = h / 360;
        return Hex(Channel(p, q, k + (1 / 3.0)), Channel(p, q, k), Channel(p, q, k - (1 / 3.0)));
    }

    private static byte Channel(double p, double q, double t)
    {
        t = t < 0 ? t + 1 : t > 1 ? t - 1 : t;
        var v = t < 1 / 6.0 ? p + ((q - p) * 6 * t) : t < 0.5 ? q : t < 2 / 3.0 ? p + ((q - p) * ((2 / 3.0) - t) * 6) : p;
        return (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
    }

    private static string Hex(byte r, byte g, byte b) => string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");
}
