using System.Globalization;
using System.Text;

namespace TMConcept.Core.Pronunciation;

/// <summary>
/// Replaces whole words of a narration with how the voice should read them, before the text goes to the voice (the subtitles keep the
/// original). A word matches whole only: letters, digits and combining marks around it end the match, so "Viking" matches in "Viking'ler"
/// and "(Viking)" but not in "Vikingler". Upper and lower case follow the language's culture one letter at a time: in Turkish "VİKİNG"
/// matches "Viking" and "VIKING" does not (its lower case is "vıkıng"); the language's own lower case is never the invariant one, which
/// breaks I/ı. The case of the narration's word is kept on the spoken form: "VİKİNG" → "VİKİNK", "viking" → "vikink". The longest match
/// wins ("New York" before "New").
/// </summary>
public static class PronunciationRewriter
{
    /// <summary>The narration with the rules applied.</summary>
    public static string Rewrite(string text, IReadOnlyList<PronunciationRule> rules, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(culture);
        if (string.IsNullOrEmpty(text) || rules.Count == 0)
        {
            return text ?? string.Empty;
        }

        var folded = Fold(text, culture);
        var prepared = rules.Where(r => r.Word.Trim().Length > 0)
            .Select(r => (Folded: Fold(r.Word.Trim(), culture), r.Spoken))
            .OrderByDescending(r => r.Folded.Length)
            .ToList();
        var output = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (!IsWordCharacter(text[i]) || (i > 0 && IsWordCharacter(text[i - 1])))
            {
                output.Append(text[i]);
                i++;
                continue;
            }

            var match = prepared.FirstOrDefault(r => folded.AsSpan(i).StartsWith(r.Folded, StringComparison.Ordinal)
                && (i + r.Folded.Length == text.Length || !IsWordCharacter(text[i + r.Folded.Length])));
            if (match.Folded is not null)
            {
                output.Append(Cased(text.Substring(i, match.Folded.Length), match.Spoken.Trim(), culture));
                i += match.Folded.Length;
                continue;
            }

            // No rule starts here: the whole word is copied, so no rule matches inside it.
            var end = i;
            while (end < text.Length && IsWordCharacter(text[end]))
            {
                end++;
            }

            output.Append(text, i, end - i);
            i = end;
        }

        return output.ToString();
    }

    /// <summary>
    /// Lower case one letter at a time in the language's culture (keeps every index: Turkish "İ" is one letter "i", "I" one letter "ı").
    /// </summary>
    public static string Fold(string text, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(culture);
        var letters = text.ToCharArray();
        for (var i = 0; i < letters.Length; i++)
        {
            letters[i] = culture.TextInfo.ToLower(letters[i]);
        }

        return new string(letters);
    }

    /// <summary>Whether a character is part of a word (letter, digit, combining mark).</summary>
    public static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c)
        || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;

    /// <summary>The spoken form in the case of the narration's word: all capitals, all small, a capital first letter, or as written.</summary>
    private static string Cased(string original, string spoken, CultureInfo culture)
    {
        var letters = original.Where(char.IsLetter).ToList();
        if (letters.Count == 0 || spoken.Length == 0)
        {
            return spoken;
        }

        if (letters.Count > 1 && letters.All(char.IsUpper))
        {
            return spoken.ToUpper(culture);
        }

        if (letters.All(char.IsLower))
        {
            return spoken.ToLower(culture);
        }

        return char.IsUpper(letters[0]) && char.IsLetter(spoken[0]) ? culture.TextInfo.ToUpper(spoken[0]) + spoken[1..] : spoken;
    }
}
