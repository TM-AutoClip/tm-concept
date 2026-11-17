using System.Globalization;
using System.Text;

namespace TMConcept.Core.Text;

/// <summary>
/// Cleans untrusted text (from a model, a document, a fetched page): NUL and other control characters removed (line breaks and tabs kept),
/// the invisible format characters that hide or reorder text removed (<see cref="Visible"/>), CRLF → LF, trimmed, cut.
/// </summary>
public static class TextClean
{
    /// <summary>The cleaned text, at most <paramref name="max"/> characters.</summary>
    public static string Clean(string? text, int max)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var chars = Visible(text).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Where(c => c == '\n' || c == '\t' || !char.IsControl(c))
            .ToArray();
        var result = new string(chars).Trim();
        return result.Length <= max ? result : result[..max].TrimEnd();
    }

    /// <summary>One line: line breaks become spaces.</summary>
    public static string OneLine(string? text, int max) =>
        Clean(text?.Replace('\n', ' ').Replace('\r', ' '), max);

    /// <summary>
    /// <paramref name="text"/> without invisible format characters (Unicode category Cf: zero-width spaces, bidi overrides and isolates, the
    /// byte-order mark, soft hyphens, tag characters) except the zero-width joiner and non-joiner, which Persian, Arabic and Indic scripts
    /// (and emoji) need. Text that is data for a model says what it says in plain sight: a page or a PDF cannot hide "ignore the rules" in
    /// characters a reader does not see.
    /// </summary>
    public static string Visible(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var any = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Hidden(rune))
            {
                any = true;
                break;
            }
        }

        if (!any)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (!Hidden(rune))
            {
                builder.Append(rune.ToString());
            }
        }

        return builder.ToString();
    }

    private static bool Hidden(Rune rune) => Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format && rune.Value is not (0x200C or 0x200D);
}
