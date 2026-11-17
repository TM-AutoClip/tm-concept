using System.Globalization;
using System.Text.RegularExpressions;

namespace TMConcept.Core.Styles;

/// <summary>
/// Colour literals and palette tokens: reads a CSS colour literal (<c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>,
/// <c>rgb()</c>, <c>rgba()</c>), gives a token's value and finds the nearest token of a colour. The style check uses it to replace a colour
/// outside the palette with its nearest token without a paid repair.
/// </summary>
public static partial class PaletteColors
{
    /// <summary>
    /// Reads a single colour literal (surrounding spaces allowed; case does not matter). <c>false</c> for anything else (named colours,
    /// <c>hsl()</c>, broken literals): those are left to the caller.
    /// </summary>
    public static bool TryParse(string? literal, out ColorValue color)
    {
        color = default;
        var text = literal?.Trim() ?? string.Empty;
        var hex = Hex().Match(text);
        if (hex.Success)
        {
            var digits = hex.Groups[1].Value;
            if (digits.Length is 3 or 4)
            {
                digits = string.Concat(digits.Select(c => new string(c, 2)));
            }

            var r = byte.Parse(digits.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var g = byte.Parse(digits.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var b = byte.Parse(digits.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var a = digits.Length == 8 ? byte.Parse(digits.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0 : 1.0;
            color = new ColorValue(r, g, b, a);
            return true;
        }

        var rgb = Rgb().Match(text);
        if (!rgb.Success)
        {
            return false;
        }

        var parts = rgb.Groups[1].Value.Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries);
        var slash = rgb.Groups[1].Value.Contains('/', StringComparison.Ordinal);
        var comma = rgb.Groups[1].Value.Contains(',', StringComparison.Ordinal);
        if (parts.Length is not (3 or 4) || (slash && comma) || (parts.Length == 4 && !slash && !comma))
        {
            return false;
        }

        if (!Channel(parts[0], out var red) || !Channel(parts[1], out var green) || !Channel(parts[2], out var blue))
        {
            return false;
        }

        var alpha = 1.0;
        if (parts.Length == 4 && !Alpha(parts[3], out alpha))
        {
            return false;
        }

        color = new ColorValue(red, green, blue, alpha);
        return true;
    }

    /// <summary>The #RRGGBB value of a token; throws <see cref="ArgumentException"/> for a name not in <see cref="PaletteTokens.All"/>.</summary>
    public static string Value(VideoPalette palette, string token)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return token switch
        {
            PaletteTokens.Background => palette.Background,
            PaletteTokens.Surface => palette.Surface,
            PaletteTokens.Accent => palette.Accent,
            PaletteTokens.Accent2 => palette.Accent2,
            PaletteTokens.Text => palette.Text,
            PaletteTokens.Muted => palette.Muted,
            _ => throw new ArgumentException($"Unknown palette token '{token}'.", nameof(token)),
        };
    }

    /// <summary>The CSS custom property reference of a token (<c>var(--tm-accent)</c>).</summary>
    public static string CssVariable(string token)
    {
        if (!PaletteTokens.All.Contains(token, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Unknown palette token '{token}'.", nameof(token));
        }

        return $"var({PaletteTokens.CssPrefix}{token})";
    }

    /// <summary>
    /// The token whose colour is nearest (perceptual "redmean" distance; opacity is not compared). Ties go to the token that comes first in
    /// <see cref="PaletteTokens.All"/>.
    /// </summary>
    public static string Nearest(VideoPalette palette, ColorValue color)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var best = PaletteTokens.All[0];
        var bestDistance = double.MaxValue;
        foreach (var token in PaletteTokens.All)
        {
            var distance = Distance(color, Parse(Value(palette, token)));
            if (distance < bestDistance)
            {
                best = token;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>The colour is exactly one of the palette's tokens (red, green and blue; opacity is not compared).</summary>
    public static bool InPalette(VideoPalette palette, ColorValue color)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return PaletteTokens.All.Select(t => Parse(Value(palette, t))).Any(c => c.R == color.R && c.G == color.G && c.B == color.B);
    }

    /// <summary>Squared "redmean" distance of two colours (a cheap approximation of how different they look).</summary>
    public static double Distance(ColorValue first, ColorValue second)
    {
        var meanRed = (first.R + second.R) / 2.0;
        var dr = first.R - second.R;
        var dg = first.G - second.G;
        var db = first.B - second.B;
        return ((2 + (meanRed / 256)) * dr * dr) + (4 * dg * dg) + ((2 + ((255 - meanRed) / 256)) * db * db);
    }

    private static ColorValue Parse(string hex) =>
        TryParse(hex, out var color) ? color : throw new InvalidOperationException($"Palette colour '{hex}' is not a colour.");

    private static bool Channel(string part, out byte value)
    {
        value = 0;
        if (part.EndsWith('%'))
        {
            if (!double.TryParse(part[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) || percent is < 0 or > 100)
            {
                return false;
            }

            value = (byte)Math.Round(percent * 255 / 100, MidpointRounding.AwayFromZero);
            return true;
        }

        if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number is < 0 or > 255)
        {
            return false;
        }

        value = (byte)Math.Round(number, MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool Alpha(string part, out double value)
    {
        value = 1;
        var percent = part.EndsWith('%');
        if (!double.TryParse(percent ? part[..^1] : part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        value = percent ? number / 100 : number;
        return value is >= 0 and <= 1;
    }

    [GeneratedRegex(@"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex Hex();

    [GeneratedRegex(@"^rgba?\(\s*([0-9.%,/\s]+?)\s*\)$", RegexOptions.IgnoreCase)]
    private static partial Regex Rgb();
}
