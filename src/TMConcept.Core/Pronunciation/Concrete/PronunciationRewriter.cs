using System.Globalization;
using System.Text;

namespace TMConcept.Core.Pronunciation;

/// <summary>
/// Replaces whole words of a narration with how the voice should read them, before the text goes to the voice (the subtitles keep the
/// original). A word matches whole only: letters, digits and combining marks around it end the match, so "Viking" matches in "Viking'ler"
/// and "(Viking)" but not in "Vikingler". Upper and lower case follow the language's culture one letter at a time: in Turkish "VİKİNG"
/// matches "Viking" and "VIKING" does not (its lower case is "vıkıng"); the language's own lower case is never the invariant one, which
/// breaks I/ı. The case of the narration's word is kept on the spoken form: "VİKİNG" → "VİKİNK", "viking" → "vikink". The longest match
/// wins ("New York" before "New"). A rule marked <see cref="PronunciationRule.ExactCase"/> matches only as written ("IT", not "it") and its
/// spoken form is kept as written. With a <c>join</c> (a language that agrees suffixes, Turkish) the suffix after an apostrophe is joined
/// to the spoken form and agreed with it ("Google'ın" → "Gugılın"), and a word in capitals gets the spoken form as written (the
/// normalizer after the dictionary would otherwise spell the capitals).
/// </summary>
public static class PronunciationRewriter
{
    /// <summary>The narration with the rules applied.</summary>
    public static string Rewrite(string text, IReadOnlyList<PronunciationRule> rules, CultureInfo culture, Func<string, string, string>? join = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(culture);
        if (string.IsNullOrEmpty(text) || rules.Count == 0)
        {
            return text ?? string.Empty;
        }

        var folded = Fold(text, culture);
        var prepared = rules.Where(r => r.Word.Trim().Length > 0)
            .Select(r => (Folded: r.ExactCase ? r.Word.Trim() : Fold(r.Word.Trim(), culture), r.Spoken, r.ExactCase))
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

            var match = prepared.FirstOrDefault(r => (r.ExactCase ? text : folded).AsSpan(i).StartsWith(r.Folded, StringComparison.Ordinal)
                && (i + r.Folded.Length == text.Length || !IsWordCharacter(text[i + r.Folded.Length])));
            if (match.Folded is not null)
            {
                var original = text.Substring(i, match.Folded.Length);
                var spoken = match.ExactCase ? match.Spoken.Trim() : Cased(original, match.Spoken.Trim(), culture, keepCapitals: join is null);
                i += match.Folded.Length;
                if (join is not null && Suffix(text, i) is { Length: > 0 } suffix)
                {
                    spoken = join(spoken, suffix);
                    i += suffix.Length + 1;
                }

                output.Append(spoken);
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

    /// <summary>The letters right after an apostrophe at <paramref name="index"/> (a suffix: "Google'ın" → "ın"); empty when there is none.</summary>
    private static string Suffix(string text, int index)
    {
        if (index + 1 >= text.Length || text[index] is not ('\'' or '’') || !char.IsLetter(text[index + 1]))
        {
            return string.Empty;
        }

        var end = index + 1;
        while (end < text.Length && IsWordCharacter(text[end]))
        {
            end++;
        }

        return text[(index + 1)..end];
    }

    /// <summary>
    /// The spoken form in the case of the narration's word: all capitals (or as written when <paramref name="keepCapitals"/> is off), all
    /// small, a capital first letter, or as written.
    /// </summary>
    private static string Cased(string original, string spoken, CultureInfo culture, bool keepCapitals)
    {
        var letters = original.Where(char.IsLetter).ToList();
        if (letters.Count == 0 || spoken.Length == 0)
        {
            return spoken;
        }

        if (letters.Count > 1 && letters.All(char.IsUpper))
        {
            return keepCapitals ? spoken.ToUpper(culture) : spoken;
        }

        if (letters.All(char.IsLower))
        {
            return spoken.ToLower(culture);
        }

        return char.IsUpper(letters[0]) && char.IsLetter(spoken[0]) ? culture.TextInfo.ToUpper(spoken[0]) + spoken[1..] : spoken;
    }
}
