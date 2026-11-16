using System.Text;

namespace TMConcept.Core.Text;

/// <summary>
/// Agrees a suffix written after an apostrophe with how its stem is spoken ("Google'ın" is spoken "Gugılın", "TBMM'de" "te be me mede",
/// "1453'te" "bin dört yüz elli üçte"). Steps: (1) the buffer letter: after a spoken vowel a suffix that starts with a vowel gets "y" (the
/// genitive "-ın" gets "n"), after a spoken consonant a "y" or "n" the writer added for the written form is dropped; (2) the first d/t and
/// c/ç follow the last sound (voiceless → t, ç); (3) every a/e follows two-way harmony and every ı/i/u/ü four-way harmony with the vowel
/// before it (o/ö of "-yor" stay). <see cref="Attach"/> also softens the number word "dört" before a vowel ("dördü").
/// </summary>
public static class TurkishHarmony
{
    /// <summary>The suffix agreed with <paramref name="stem"/> (in lower case).</summary>
    public static string Suffix(string stem, string suffix)
    {
        ArgumentNullException.ThrowIfNull(stem);
        ArgumentNullException.ThrowIfNull(suffix);
        if (suffix.Length == 0)
        {
            return suffix;
        }

        var lastLetter = TurkishLetters.LastLetter(stem);
        var text = Buffer(TurkishLetters.Lower(suffix), lastLetter is { } l && TurkishLetters.IsVowel(l));
        var previousVowel = TurkishLetters.LastVowel(stem) ?? 'e';
        var previousLetter = lastLetter ?? 'a';
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (i == 0 && c is 'd' or 't')
            {
                c = TurkishLetters.IsVoiceless(previousLetter) ? 't' : 'd';
            }
            else if (i == 0 && c is 'c' or 'ç')
            {
                c = TurkishLetters.IsVoiceless(previousLetter) ? 'ç' : 'c';
            }
            else if (c is 'a' or 'e')
            {
                c = TurkishLetters.TwoWay(previousVowel);
            }
            else if (c is 'ı' or 'i' or 'u' or 'ü')
            {
                c = TurkishLetters.FourWay(previousVowel);
            }

            if (TurkishLetters.IsVowel(c))
            {
                previousVowel = c;
            }

            previousLetter = c;
            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>The stem with the agreed suffix joined to it (no apostrophe): "Gugıl" + "'ın" → "Gugılın", "dört" + "ü" → "dördü".</summary>
    public static string Attach(string stem, string suffix)
    {
        ArgumentNullException.ThrowIfNull(stem);
        ArgumentNullException.ThrowIfNull(suffix);
        if (suffix.Length == 0)
        {
            return stem;
        }

        var agreed = Suffix(stem, suffix);
        if (agreed.Length > 0 && TurkishLetters.IsVowel(agreed[0]) && TurkishLetters.Lower(stem).EndsWith("dört", StringComparison.Ordinal)
            && (stem.Length == 4 || !char.IsLetter(stem[^5])))
        {
            stem = stem[..^1] + "d";
        }

        return stem + agreed;
    }

    private static string Buffer(string suffix, bool stemEndsWithVowel)
    {
        if (stemEndsWithVowel)
        {
            if (!TurkishLetters.IsVowel(suffix[0]))
            {
                return suffix;
            }

            // The genitive (-ın/-in/-un/-ün) takes "n", the others (dative, accusative) take "y".
            return (suffix.Length == 2 && suffix[1] == 'n' ? "n" : "y") + suffix;
        }

        if (suffix.Length > 1 && suffix[0] == 'y' && TurkishLetters.IsVowel(suffix[1]))
        {
            return suffix[1..];
        }

        return suffix.Length > 2 && suffix[0] == 'n' && TurkishLetters.IsVowel(suffix[1]) && suffix[2] == 'n' ? suffix[1..] : suffix;
    }
}
