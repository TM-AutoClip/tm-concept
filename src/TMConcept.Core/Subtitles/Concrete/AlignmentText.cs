using System.Text;

namespace TMConcept.Core.Subtitles;

/// <summary>
/// The form in which the script and the heard text are compared (0.1.48, F5): lower case with the Turkish dotted and dotless i folded into
/// one letter (İ, I, ı and i are all "i"), so "İSTANBUL", "Istanbul" and "istanbul", or "IŞIK" and "ışık", are the same word whatever the
/// language and whatever culture the computer runs with; only letters and digits stay (punctuation and spaces carry no sound).
/// </summary>
public static class AlignmentText
{
    /// <summary>The compared form of one word (empty when it has no letter or digit: "—", "…").</summary>
    public static string Fold(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var text = new StringBuilder(word.Length);
        foreach (var c in word.Normalize(NormalizationForm.FormC))
        {
            if (c is 'I' or 'İ' or 'ı')
            {
                text.Append('i');
            }
            else if (char.IsLetterOrDigit(c))
            {
                text.Append(char.ToLowerInvariant(c));
            }
        }

        return text.ToString();
    }
}
