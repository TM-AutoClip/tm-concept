using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TMConcept.Core.Text;

/// <summary>
/// Spells out the numbers of an English text ("In 672 about 1,500 ships, 3.5 % of them" → "In six hundred seventy-two about one thousand five
/// hundred ships, three point five percent of them"). A voice that reads words from a dictionary (Kokoro through sherpa-onnx without
/// espeak-ng) would skip digits otherwise. Whole numbers up to 999 999 999 999; longer digit runs are read digit by digit.
/// </summary>
public static partial class EnglishNumbers
{
    private static readonly string[] Small =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen",
        "sixteen", "seventeen", "eighteen", "nineteen",
    ];

    private static readonly string[] Tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    private static readonly (long Value, string Name)[] Scales = [(1_000_000_000, "billion"), (1_000_000, "million"), (1_000, "thousand")];

    /// <summary>The text with every number spelled out and "%" read as "percent".</summary>
    public static string Spell(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var spelled = NumberPattern().Replace(text, m => Read(m.Value));
        return PercentPattern().Replace(spelled, " percent");
    }

    /// <summary>One number ("1,500", "3.5", "007").</summary>
    public static string Read(string number)
    {
        ArgumentNullException.ThrowIfNull(number);
        var parts = number.Replace(",", string.Empty, StringComparison.Ordinal).Split('.');
        var whole = parts[0].Length is > 0 and <= 12 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? Words(value)
            : Digits(parts[0]);
        return parts.Length > 1 && parts[1].Length > 0 ? whole + " point " + Digits(parts[1]) : whole;
    }

    /// <summary>A whole number in words.</summary>
    public static string Words(long value)
    {
        if (value < 0)
        {
            return "minus " + Words(-value);
        }

        if (value < 20)
        {
            return Small[value];
        }

        var builder = new StringBuilder();
        foreach (var (scale, name) in Scales)
        {
            if (value >= scale)
            {
                builder.Append(Words(value / scale)).Append(' ').Append(name);
                value %= scale;
                if (value > 0)
                {
                    builder.Append(' ');
                }
            }
        }

        if (value >= 100)
        {
            builder.Append(Small[value / 100]).Append(" hundred");
            value %= 100;
            if (value > 0)
            {
                builder.Append(' ');
            }
        }

        if (value >= 20)
        {
            builder.Append(Tens[value / 10]);
            if (value % 10 > 0)
            {
                builder.Append('-').Append(Small[value % 10]);
            }
        }
        else if (value > 0)
        {
            builder.Append(Small[value]);
        }

        return builder.ToString();
    }

    private static string Digits(string digits) => string.Join(' ', digits.Where(char.IsAsciiDigit).Select(d => Small[d - '0']));

    [GeneratedRegex(@"\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\s*%", RegexOptions.CultureInvariant)]
    private static partial Regex PercentPattern();
}
