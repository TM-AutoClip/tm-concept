using System.Globalization;

namespace TMConcept.Core.Styles;

/// <summary>
/// The user's own colour palettes (0.1.24): the contrast check every own palette must pass (WCAG 2 contrast ratios: text on the background
/// at least 4.5:1, text on the surface at least 3:1; a weaker accent or quiet text is only a warning), a harmonious palette made from one base
/// colour, and the #RRGGBB form the app stores.
/// </summary>
public static class PaletteDesigner
{
    /// <summary>Least contrast of text on the background (WCAG AA body text).</summary>
    public const double TextMin = 4.5;

    /// <summary>Least contrast of text on the surface, of the accent and of quiet text on the background (WCAG AA large text and shapes).</summary>
    public const double LargeMin = 3.0;

    /// <summary>The #RRGGBB form (upper case) of a colour typed as #rgb, #rrggbb or rrggbb; <c>null</c> when it is not one.</summary>
    public static string? Normalize(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length > 0 && text[0] != '#')
        {
            text = "#" + text;
        }

        return text.Length is 4 or 7 && PaletteColors.TryParse(text, out var color)
            ? string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}")
            : null;
    }

    /// <summary>Checks a palette's colours and contrasts.</summary>
    public static PaletteCheckResult Check(VideoPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var colours = new[] { palette.Background, palette.Surface, palette.Accent, palette.Accent2, palette.Text, palette.Muted };
        if (colours.Any(c => Normalize(c) is null || c.Length != 7))
        {
            return new PaletteCheckResult([PaletteIssue.InvalidColour], [], 0);
        }

        var errors = new List<PaletteIssue>();
        var warnings = new List<PaletteIssue>();
        var text = PaletteColors.Contrast(palette.Text, palette.Background);
        if (text < TextMin)
        {
            errors.Add(PaletteIssue.TextOnBackground);
        }

        if (PaletteColors.Contrast(palette.Text, palette.Surface) < LargeMin)
        {
            errors.Add(PaletteIssue.TextOnSurface);
        }

        if (PaletteColors.Contrast(palette.Accent, palette.Background) < LargeMin)
        {
            warnings.Add(PaletteIssue.AccentOnBackground);
        }

        if (PaletteColors.Contrast(palette.Muted, palette.Background) < LargeMin)
        {
            warnings.Add(PaletteIssue.MutedOnBackground);
        }

        return new PaletteCheckResult(errors, warnings, Math.Round(text, 2));
    }

    /// <summary>
    /// The palette with <see cref="VideoPalette.Light"/> set from its background (a light background means dark text) and every colour in the
    /// #RRGGBB form; the id is <see cref="VideoPalettes.CustomId"/>.
    /// </summary>
    public static VideoPalette Own(string background, string surface, string accent, string accent2, string text, string muted)
    {
        string Fix(string value, string name) => Normalize(value) ?? throw new ArgumentException($"'{value}' is not a colour.", name);
        var back = Fix(background, nameof(background));
        return new VideoPalette(VideoPalettes.CustomId, back, Fix(surface, nameof(surface)), Fix(accent, nameof(accent)), Fix(accent2, nameof(accent2)),
            Fix(text, nameof(text)), Fix(muted, nameof(muted)), PaletteColors.Luminance(Parse(back)) > 0.4);
    }

    /// <summary>
    /// A harmonious palette from one base colour: the background and surface in the base hue (very dark, or very light for
    /// <paramref name="light"/>), the base colour as the accent (its lightness moved until it stands out), the split-complementary hue as the
    /// second accent, a lightly tinted text and quiet text. Every contrast of <see cref="Check"/> is met; the result always passes it.
    /// </summary>
    public static VideoPalette FromBase(string baseColour, bool light)
    {
        var hex = Normalize(baseColour) ?? throw new ArgumentException($"'{baseColour}' is not a colour.", nameof(baseColour));
        var (h, s, _) = ToHsl(Parse(hex));
        var tint = Math.Min(s, 0.55);
        var background = Hsl(h, tint, light ? 0.96 : 0.09);
        var surface = Hsl(h, tint, light ? 0.99 : 0.16);
        var text = Hsl(h, Math.Min(s, 0.25), light ? 0.11 : 0.97);
        var muted = Hsl(h, Math.Min(s, 0.15), light ? 0.38 : 0.70);
        var accent = Hsl(h, Math.Max(s, 0.65), light ? 0.42 : 0.60);
        var accent2 = Hsl((h + 150) % 360, Math.Max(s, 0.55), light ? 0.40 : 0.65);

        // Move a colour away from the background until it stands out enough (darker on a light page, lighter on a dark one).
        string Away(string colour, double min)
        {
            var (ch, cs, cl) = ToHsl(Parse(colour));
            for (var step = 0; step < 40 && PaletteColors.Contrast(colour, background) < min; step++)
            {
                cl = Math.Clamp(cl + (light ? -0.02 : 0.02), 0, 1);
                colour = Hsl(ch, cs, cl);
            }

            return colour;
        }

        text = Away(text, PaletteDesigner.TextMin + 0.5);
        muted = Away(muted, LargeMin + 0.3);
        accent = Away(accent, LargeMin + 0.3);
        accent2 = Away(accent2, LargeMin);
        return Own(background, surface, accent, accent2, text, muted);
    }

    private static ColorValue Parse(string hex) =>
        PaletteColors.TryParse(hex, out var color) ? color : throw new ArgumentException($"'{hex}' is not a colour.", nameof(hex));

    private static (double H, double S, double L) ToHsl(ColorValue color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-9)
        {
            return (0, 0, l);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? ((g - b) / d) + (g < b ? 6 : 0) : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
        return (h * 60, s, l);
    }

    private static string Hsl(double h, double s, double l)
    {
        double Channel(double n)
        {
            var k = (n + (h / 30)) % 12;
            var a = s * Math.Min(l, 1 - l);
            return l - (a * Math.Max(-1, Math.Min(k - 3, Math.Min(9 - k, 1))));
        }

        static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
        return string.Create(CultureInfo.InvariantCulture, $"#{Byte(Channel(0)):X2}{Byte(Channel(8)):X2}{Byte(Channel(4)):X2}");
    }
}
