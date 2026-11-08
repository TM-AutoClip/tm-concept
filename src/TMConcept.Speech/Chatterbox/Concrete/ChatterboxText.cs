using System.Globalization;
using System.Text;
using TMConcept.Core.Profiles;

namespace TMConcept.Speech.Chatterbox;

/// <summary>
/// Text preparation of Chatterbox: a scene is cut into parts of at most <see cref="ChatterboxModel.MaxPartCharacters"/> characters at
/// sentence ends (a longer sentence at a comma, else at a space), and each part gets upstream's punc_norm: white space collapsed, "…" / ":" /
/// ";" / " - " to commas, dashes and curly quotes to plain ones, a full stop added when the part ends without one, the first letter in capitals.
/// One deliberate difference: the capital of a Turkish "i" is "İ" (Python's str.upper() gives "I", which reads as "ı").
/// </summary>
internal static class ChatterboxText
{
    private static readonly (string From, string To)[] Replacements =
    [
        ("...", ", "), ("…", ", "), (":", ","), (" - ", ", "), (";", ", "), ("—", "-"), ("–", "-"), (" ,", ","),
        ("“", "\""), ("”", "\""), ("‘", "'"), ("’", "'"),
    ];

    private static readonly string[] Enders = [".", "!", "?", "-", ",", "、", "，", "。", "？", "！"];

    /// <summary>The parts of a scene, each ready for the tokenizer (empty: nothing to say).</summary>
    public static List<string> Parts(string text, string language)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = new List<string>();
        var current = new StringBuilder();
        foreach (var sentence in Sentences(Collapse(text)))
        {
            foreach (var piece in Fit(sentence, ChatterboxModel.MaxPartCharacters))
            {
                if (current.Length > 0 && current.Length + 1 + piece.Length > ChatterboxModel.MaxPartCharacters)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                if (current.Length > 0)
                {
                    current.Append(' ');
                }

                current.Append(piece);
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts.Select(p => Normalize(p, language)).Where(p => p.Length > 0).ToList();
    }

    /// <summary>Upstream's punc_norm of one part.</summary>
    public static string Normalize(string text, string language)
    {
        ArgumentNullException.ThrowIfNull(text);
        var clean = Collapse(text);
        if (clean.Length == 0)
        {
            return string.Empty;
        }

        if (char.IsLower(clean[0]))
        {
            var culture = language == ContentLanguages.Turkish ? CultureInfo.GetCultureInfo("tr-TR") : CultureInfo.InvariantCulture;
            clean = char.ToUpper(clean[0], culture) + clean[1..];
        }

        foreach (var (from, to) in Replacements)
        {
            clean = clean.Replace(from, to, StringComparison.Ordinal);
        }

        clean = clean.TrimEnd(' ');
        if (!Enders.Any(e => clean.EndsWith(e, StringComparison.Ordinal)))
        {
            clean += ".";
        }

        return clean;
    }

    /// <summary>Python's " ".join(text.split()): every run of white space becomes one space, none at the ends.</summary>
    private static string Collapse(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Sentences: a cut after ".", "!", "?" or "…" (with closing quotes or brackets after it) followed by a space.</summary>
    private static List<string> Sentences(string text)
    {
        var sentences = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('.' or '!' or '?' or '…'))
            {
                continue;
            }

            var end = i + 1;
            while (end < text.Length && text[end] is '"' or '\'' or '”' or '’' or ')' or '»' or '.' or '!' or '?')
            {
                end++;
            }

            if (end < text.Length && text[end] == ' ')
            {
                sentences.Add(text[start..end]);
                start = end + 1;
                i = end;
            }
        }

        if (start < text.Length)
        {
            sentences.Add(text[start..]);
        }

        return sentences.Where(s => s.Length > 0).ToList();
    }

    /// <summary>A sentence longer than <paramref name="max"/> in pieces: at the last comma before the limit, else at the last space.</summary>
    private static List<string> Fit(string sentence, int max)
    {
        var pieces = new List<string>();
        var rest = sentence;
        while (rest.Length > max)
        {
            var cut = rest.LastIndexOf(", ", max - 1, StringComparison.Ordinal);
            var at = cut > max / 3 ? cut + 1 : rest.LastIndexOf(' ', max - 1);
            if (at <= 0)
            {
                at = max;
            }

            pieces.Add(rest[..at].Trim());
            rest = rest[at..].Trim();
        }

        if (rest.Length > 0)
        {
            pieces.Add(rest);
        }

        return pieces;
    }
}
