using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TMConcept.Core.Text;

/// <summary>
/// Spells out the numbers of an English text ("In 672 about 1,500 ships, 3.5 % of them" → "In six hundred seventy two about one thousand five
/// hundred ships, three point five percent of them"). A voice that reads words from a dictionary (Kokoro through sherpa-onnx without
/// espeak-ng) would skip digits otherwise, so every word written here is a plain dictionary word: tens and units are joined with a space
/// (the dictionary lacks most "seventy-two" forms), ordinals ("21st" → "twenty first") and decades ("1990s" → "nineteen nineties") are whole
/// words, and a year-like number 1100–1999 is read in pairs ("1453" → "fourteen fifty three"). Whole numbers up to 999 999 999 999; longer
/// digit runs are read digit by digit.
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
        var spelled = NumberPattern().Replace(text, m => Suffixed(m.Groups["n"].Value, m.Groups["s"].Value));
        return PercentPattern().Replace(spelled, " percent");
    }

    /// <summary>A number with its suffix: an ordinal ("21st"), a plural or decade ("1990s", "80's"), or none.</summary>
    private static string Suffixed(string number, string suffix)
    {
        var plain = number.Length is 4 && !number.Contains('.', StringComparison.Ordinal) && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            && year is >= 1100 and <= 1999
            ? Year(year)
            : Read(number);
        if (suffix.Length == 0 || number.Contains('.', StringComparison.Ordinal))
        {
            return plain + suffix;
        }

        var at = plain.LastIndexOf(' ') + 1;
        var head = plain[..at];
        var last = plain[at..];
        return suffix.ToLowerInvariant() switch
        {
            "st" or "nd" or "rd" or "th" => head + Ordinal(last),
            _ => head + Plural(last),
        };
    }

    /// <summary>A year 1100–1999 in pairs: "fourteen fifty three", "nineteen oh five", "fifteen hundred".</summary>
    private static string Year(int year)
    {
        var high = year / 100;
        var low = year % 100;
        return low == 0 ? Words(high) + " hundred" : low < 10 ? Words(high) + " oh " + Words(low) : Words(high) + " " + Words(low);
    }

    private static string Ordinal(string word) => word switch
    {
        "one" => "first",
        "two" => "second",
        "three" => "third",
        "five" => "fifth",
        "eight" => "eighth",
        "nine" => "ninth",
        "twelve" => "twelfth",
        _ when word.EndsWith('y') => word[..^1] + "ieth",
        _ => word + "th",
    };

    private static string Plural(string word) => word switch
    {
        _ when word.EndsWith('y') => word[..^1] + "ies",
        _ when word.EndsWith('x') => word + "es",
        _ => word + "s",
    };

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
                builder.Append(' ').Append(Small[value % 10]);
            }
        }
        else if (value > 0)
        {
            builder.Append(Small[value]);
        }

        return builder.ToString();
    }

    private static string Digits(string digits) => string.Join(' ', digits.Where(char.IsAsciiDigit).Select(d => Small[d - '0']));

    [GeneratedRegex(@"(?<n>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?:(?<s>st|nd|rd|th|'s|s)(?![A-Za-z]))?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\s*%", RegexOptions.CultureInvariant)]
    private static partial Regex PercentPattern();
}
