using System.Globalization;

namespace TMConcept.Core.Text;

/// <summary>
/// Turkish letters for the spoken text: lower and upper case with I/ı and İ/i done by hand (so a system without the Turkish culture data
/// still gets them right), vowel classes (back/front, voiceless consonants: "fıstıkçı şahap") and the two vowel harmonies. Every case
/// change keeps the length of the text.
/// </summary>
public static class TurkishLetters
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Turkish lower case ("I" → "ı", "İ" → "i").</summary>
    public static string Lower(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace('I', 'ı').Replace('İ', 'i').ToLower(Turkish);
    }

    /// <summary>Turkish upper case ("i" → "İ", "ı" → "I").</summary>
    public static string Upper(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace('i', 'İ').Replace('ı', 'I').ToUpper(Turkish);
    }

    /// <summary>The first letter in upper case, the rest as it is ("ikinci" → "İkinci").</summary>
    public static string Capitalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length == 0 ? text : Upper(text[..1]) + text[1..];
    }

    /// <summary>A capital first letter and the rest in lower case ("NATO" → "Nato").</summary>
    public static string Title(string text) => Capitalize(Lower(text));

    /// <summary>Whether a lower case letter is a Turkish vowel (circumflex vowels too).</summary>
    public static bool IsVowel(char c) => c is 'a' or 'e' or 'ı' or 'i' or 'o' or 'ö' or 'u' or 'ü' or 'â' or 'î' or 'û';

    /// <summary>Whether a lower case vowel is a back vowel (a, ı, o, u).</summary>
    public static bool IsBackVowel(char c) => c is 'a' or 'ı' or 'o' or 'u' or 'â' or 'û';

    /// <summary>Whether a lower case letter is a voiceless consonant (f, s, t, k, ç, ş, h, p).</summary>
    public static bool IsVoiceless(char c) => c is 'f' or 's' or 't' or 'k' or 'ç' or 'ş' or 'h' or 'p';

    /// <summary>The last vowel of the text in lower case (circumflex dropped); null when it has none.</summary>
    public static char? LastVowel(string text)
    {
        var lower = Lower(text);
        for (var i = lower.Length - 1; i >= 0; i--)
        {
            if (IsVowel(lower[i]))
            {
                return Plain(lower[i]);
            }
        }

        return null;
    }

    /// <summary>The last letter of the text in lower case (circumflex dropped); null when it has none.</summary>
    public static char? LastLetter(string text)
    {
        var lower = Lower(text);
        for (var i = lower.Length - 1; i >= 0; i--)
        {
            if (char.IsLetter(lower[i]))
            {
                return Plain(lower[i]);
            }
        }

        return null;
    }

    /// <summary>Two-way harmony: "a" after a back vowel, "e" after a front one.</summary>
    public static char TwoWay(char previousVowel) => IsBackVowel(previousVowel) ? 'a' : 'e';

    /// <summary>Four-way harmony: a/ı → ı, e/i → i, o/u → u, ö/ü → ü.</summary>
    public static char FourWay(char previousVowel) => previousVowel switch
    {
        'a' or 'ı' or 'â' => 'ı',
        'o' or 'u' or 'û' => 'u',
        'ö' or 'ü' => 'ü',
        _ => 'i',
    };

    private static char Plain(char c) => c switch
    {
        'â' => 'a',
        'î' => 'i',
        'û' => 'u',
        _ => c,
    };
}
