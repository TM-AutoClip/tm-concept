using System.Globalization;
using System.Text;

namespace TMConcept.Core.Documents;

/// <summary>
/// Whether a PDF page's text layer can be used as it is (0.1.27). A layer that is not empty can still be useless: a font without a Unicode
/// map, a Type 3 font or an odd encoding gives replacement characters, private-use code points or control characters instead of letters, and
/// a scan with a few stray characters on it has almost no text at all. Such a page goes the way of a page without text (drawn by the PDF
/// renderer, then read by the OCR or the vision model). A page with real text over a large picture keeps its text.
/// </summary>
public static class PageTextQuality
{
    /// <summary>Letters and digits a page needs at least.</summary>
    public const int LettersMin = 3;

    /// <summary>The share of unreadable characters (of all that are not white space) above which the layer is not used.</summary>
    public const double UnreadableShareMax = 0.3;

    /// <summary>Whether <paramref name="text"/> is a usable text layer.</summary>
    public static bool Usable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var letters = 0;
        var visible = 0;
        var unreadable = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                continue;
            }

            visible++;
            if (Rune.IsLetterOrDigit(rune))
            {
                letters++;
            }
            else if (Unreadable(rune))
            {
                unreadable++;
            }
        }

        return letters >= LettersMin && unreadable <= visible * UnreadableShareMax;
    }

    /// <summary>A character a broken font map gives instead of a letter: the replacement character, private use, controls, unassigned.</summary>
    private static bool Unreadable(Rune rune) => rune.Value == 0xFFFD || Rune.GetUnicodeCategory(rune) is UnicodeCategory.PrivateUse or UnicodeCategory.Control
        or UnicodeCategory.OtherNotAssigned or UnicodeCategory.Surrogate;
}
