using System.Globalization;

namespace TMConcept.Core.Text;

/// <summary>
/// Numbers in Turkish words, for the voice: groups of three with the scale words ("bin" alone, never "bir bin"; "yüz" alone, never "bir
/// yüz"; but "bir milyon"), "eksi" for a negative number, leading zeros read one by one ("007" → "sıfır sıfır yedi"), more than 18 digits
/// read digit by digit, decimals with "virgül" ("3,05" → "üç virgül sıfır beş"), ordinals with four-way harmony ("üçüncü", "dördüncü",
/// "altıncı", "yirmi birinci").
/// </summary>
public static class TurkishNumbers
{
    private const int MaxDigits = 18;

    private static readonly string[] Ones = ["", "bir", "iki", "üç", "dört", "beş", "altı", "yedi", "sekiz", "dokuz"];
    private static readonly string[] Tens = ["", "on", "yirmi", "otuz", "kırk", "elli", "altmış", "yetmiş", "seksen", "doksan"];
    private static readonly string[] Scales = ["", "bin", "milyon", "milyar", "trilyon", "katrilyon", "kentilyon"];

    /// <summary>Every number word (for rules that look for "a number before this"), longest first.</summary>
    public static readonly IReadOnlyList<string> Words =
        Ones.Concat(Tens).Concat(Scales).Append("yüz").Append("sıfır").Append("dörd").Append("buçuk")
            .Where(w => w.Length > 0).Distinct(StringComparer.Ordinal).OrderByDescending(w => w.Length).ToList();

    /// <summary>The words of a number.</summary>
    public static string Cardinal(long value)
    {
        if (value == 0)
        {
            return "sıfır";
        }

        var magnitude = value < 0 ? (ulong)(-(value + 1)) + 1UL : (ulong)value;
        var groups = new List<int>();
        while (magnitude > 0)
        {
            groups.Add((int)(magnitude % 1000));
            magnitude /= 1000;
        }

        var parts = new List<string>();
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var group = groups[i];
            if (group == 0)
            {
                continue;
            }

            if (!(i == 1 && group == 1))
            {
                Group(group, parts);
            }

            if (i > 0)
            {
                parts.Add(Scales[i]);
            }
        }

        var words = string.Join(' ', parts);
        return value < 0 ? "eksi " + words : words;
    }

    /// <summary>The words of a run of digits (leading zeros one by one; a very long run digit by digit). Not digits: returned as it is.</summary>
    public static string Cardinal(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
        {
            return digits;
        }

        if (digits.Length > MaxDigits)
        {
            return string.Join(' ', digits.Select(c => c == '0' ? "sıfır" : Ones[c - '0']));
        }

        var zeros = digits.Length == 1 ? 0 : digits.TakeWhile(c => c == '0').Count();
        if (zeros == digits.Length)
        {
            return string.Join(' ', Enumerable.Repeat("sıfır", zeros));
        }

        var rest = Cardinal(long.Parse(digits.AsSpan(zeros), NumberStyles.None, CultureInfo.InvariantCulture));
        return zeros == 0 ? rest : string.Join(' ', Enumerable.Repeat("sıfır", zeros)) + " " + rest;
    }

    /// <summary>A decimal number: "3" + "5" + "virgül" → "üç virgül beş" (the fraction is read as a number after its leading zeros).</summary>
    public static string Decimal(string integerDigits, string fractionDigits, string separator)
    {
        ArgumentNullException.ThrowIfNull(integerDigits);
        ArgumentNullException.ThrowIfNull(fractionDigits);
        var integer = Cardinal(integerDigits.Length == 0 ? "0" : integerDigits);
        return fractionDigits.Length == 0 ? integer : integer + " " + separator + " " + Cardinal(fractionDigits);
    }

    /// <summary>The ordinal words of a number ("3" → "üçüncü").</summary>
    public static string Ordinal(long value) => OrdinalOf(Cardinal(value));

    /// <summary>The ordinal of number words: only the last word changes ("yirmi dört" → "yirmi dördüncü").</summary>
    public static string OrdinalOf(string cardinal)
    {
        ArgumentNullException.ThrowIfNull(cardinal);
        var trimmed = cardinal.TrimEnd();
        if (trimmed.Length == 0)
        {
            return cardinal;
        }

        var split = trimmed.LastIndexOf(' ');
        var head = split < 0 ? string.Empty : trimmed[..(split + 1)];
        var last = split < 0 ? trimmed : trimmed[(split + 1)..];
        var lower = TurkishLetters.Lower(last);
        var stem = lower.EndsWith("dört", StringComparison.Ordinal) ? last[..^1] + "d" : last;
        var vowel = TurkishLetters.FourWay(TurkishLetters.LastVowel(lower) ?? 'i');
        return head + (TurkishLetters.IsVowel(lower[^1]) ? stem + "nc" + vowel : stem + vowel + "nc" + vowel);
    }

    private static void Group(int group, List<string> parts)
    {
        var hundreds = group / 100;
        var tens = group / 10 % 10;
        var ones = group % 10;
        if (hundreds > 0)
        {
            if (hundreds > 1)
            {
                parts.Add(Ones[hundreds]);
            }

            parts.Add("yüz");
        }

        if (tens > 0)
        {
            parts.Add(Tens[tens]);
        }

        if (ones > 0)
        {
            parts.Add(Ones[ones]);
        }
    }
}
