using System.Text.RegularExpressions;

namespace TMConcept.Core.Speech;

/// <summary>
/// Splits a spoken text into sentences, so the narration can pause after each one (<see cref="NarrationPacing.SentenceGapMs"/>). A sentence
/// ends at ".", "!", "?" or "…" (also with closing quotes or brackets after it) followed by a space and a capital letter, a digit or an
/// opening quote. It does not end after a Roman numeral ("II. Mehmed"), a number ("1. Dünya Savaşı"), a single letter or a known
/// abbreviation ("Dr.", "M.Ö.", "vb."), so those are never read as two sentences.
/// </summary>
public static partial class SentenceSplitter
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "dr", "prof", "doç", "yrd", "av", "sn", "bkz", "vb", "vs", "örn", "yy", "mö", "ms", "m.ö", "m.s", "st", "mr", "mrs", "ms", "no", "nr", "vol",
        "etc", "e.g", "i.e", "vs", "jr", "sr", "gen", "col", "lt", "capt", "mt", "ca", "approx", "fig", "ss", "s", "c",
    };

    /// <summary>The sentences of <paramref name="text"/>, trimmed, in order (one item when it has a single sentence; none when it is empty).</summary>
    public static IReadOnlyList<string> Split(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return [];
        }

        var sentences = new List<string>();
        var start = 0;
        foreach (Match match in Boundary().Matches(value))
        {
            var end = match.Index + match.Groups["end"].Length;
            if (IsAbbreviation(value, match.Index))
            {
                continue;
            }

            var sentence = value[start..end].Trim();
            if (sentence.Length > 0)
            {
                sentences.Add(sentence);
            }

            start = match.Index + match.Length;
        }

        var rest = value[start..].Trim();
        if (rest.Length > 0)
        {
            sentences.Add(rest);
        }

        return sentences;
    }

    /// <summary>Whether the full stop at <paramref name="at"/> closes a Roman numeral, a number, a single letter or an abbreviation.</summary>
    private static bool IsAbbreviation(string text, int at)
    {
        if (text[at] != '.')
        {
            return false;
        }

        var from = at;
        while (from > 0 && !char.IsWhiteSpace(text[from - 1]) && text[from - 1] is not ('(' or '"' or '\'' or '«'))
        {
            from--;
        }

        var word = text[from..at];
        if (word.Length == 0)
        {
            return false;
        }

        return word.All(char.IsDigit) || RomanNumeral().IsMatch(word) || (word.Length == 1 && char.IsLetter(word[0])) || Abbreviations.Contains(word)
            || Abbreviations.Contains(word.Replace(".", string.Empty, StringComparison.Ordinal));
    }

    // The end mark (with closing quotes or brackets), then spaces, then what starts the next sentence (looked at, not taken).
    [GeneratedRegex(@"(?<end>(?:[.!?]+|…)[""'»)\]]*)\s+(?=[\p{Lu}\d""'«(])")]
    private static partial Regex Boundary();

    [GeneratedRegex("^[IVXLCDM]+$")]
    private static partial Regex RomanNumeral();
}
