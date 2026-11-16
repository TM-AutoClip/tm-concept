using System.Text.RegularExpressions;

namespace TMConcept.Core.Text;

/// <summary>Roman numerals I to MMMCMXCIX; a malformed one (IIII, VX, IC) is refused.</summary>
public static partial class RomanNumerals
{
    /// <summary>The value of a well-formed Roman numeral in capitals.</summary>
    public static bool TryParse(string text, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text) || !Strict().IsMatch(text))
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var current = Of(text[i]);
            var next = i + 1 < text.Length ? Of(text[i + 1]) : 0;
            value += current < next ? -current : current;
        }

        return value > 0;
    }

    private static int Of(char c) => c switch
    {
        'I' => 1,
        'V' => 5,
        'X' => 10,
        'L' => 50,
        'C' => 100,
        'D' => 500,
        'M' => 1000,
        _ => 0,
    };

    [GeneratedRegex("^M{0,3}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex Strict();
}
