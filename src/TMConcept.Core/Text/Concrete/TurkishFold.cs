using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TMConcept.Core.Text;

/// <summary>
/// Folds text for comparison: Turkish lower case (so "I" becomes "ı", then "i"), diacritics removed (ç→c, ğ→g, ö→o, ş→s, ü→u),
/// punctuation turned into spaces, spaces collapsed. "Bizans'ın SURLARI" and "bizansin surlari" fold to the same text.
/// </summary>
public static partial class TurkishFold
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>The folded text.</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lower = text.ToLower(Turkish);
        var builder = new StringBuilder(lower.Length);
        foreach (var c in lower.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var d = c == 'ı' ? 'i' : c;
            builder.Append(char.IsLetterOrDigit(d) ? d : ' ');
        }

        return Spaces().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
