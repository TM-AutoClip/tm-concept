using TMConcept.Core.Costs;

namespace TMConcept.Core.Documents;

/// <summary>
/// Tokens of document text, counted on the safe side whatever the profile's language: the usual estimate of the text in that language, plus
/// one more token for every letter outside the Latin script (Chinese, Japanese and Korean take about one token a character, Cyrillic, Greek,
/// Arabic, Hebrew or Hindi one to two characters a token, while the language's own rate assumes three or four). A Japanese Wikipedia
/// article for an English channel thus no longer fills a small local context two or three times over.
/// </summary>
public static class PassageTokens
{
    /// <summary>Tokens a passage line takes besides its heading and text ("[2] «…»: «…»" and the line break).</summary>
    public const int LineOverhead = 12;

    /// <summary>Tokens a DOCUMENTS line takes besides its title ("2: «…»" and the line break).</summary>
    public const int DocumentOverhead = 6;

    /// <summary>The counting function of document text for <paramref name="language"/>.</summary>
    public static Func<string, long> For(ICostCalculator costs, string language)
    {
        ArgumentNullException.ThrowIfNull(costs);
        return text => Count(costs, text, language);
    }

    /// <summary>Tokens of <paramref name="text"/> (see the class summary).</summary>
    public static long Count(ICostCalculator costs, string text, string language)
    {
        ArgumentNullException.ThrowIfNull(costs);
        text ??= string.Empty;
        return costs.Tokens(text, language) + NonLatinLetters(text);
    }

    /// <summary>How many letters of <paramref name="text"/> are outside the Latin script (Latin, Latin-1 and Latin Extended letters are not counted).</summary>
    public static int NonLatinLetters(string text)
    {
        var count = 0;
        foreach (var c in text ?? string.Empty)
        {
            // A letter beyond the BMP counts once (its low surrogate); Latin Extended Additional (Vietnamese) is Latin.
            if (c > 'ɏ' && c is < 'Ḁ' or > 'ỿ' && (char.IsLetter(c) || char.IsLowSurrogate(c)))
            {
                count++;
            }
        }

        return count;
    }
}
