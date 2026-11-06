using System.Text;
using System.Text.RegularExpressions;

namespace TMConcept.Speech.Speech;

/// <summary>
/// The words of Kokoro's English dictionary, loaded in the speech worker (never in the window process). sherpa-onnx 2.x reads English for
/// Kokoro only from this dictionary and skips a word it does not hold ("OOV word skipped"). Before a scene is spoken, a hyphenated word that
/// is not in the dictionary but whose parts are ("twenty-two", "well-made") is split into its parts, and the words that are still unknown
/// (names, coined words) are reported, so the app can warn that the narration leaves them out. The check splits words the way the text
/// reaches the dictionary (lowercase; letters, digits, apostrophes and hyphens); it is close to sherpa-onnx's own split, not identical.
/// </summary>
internal sealed partial class KokoroLexicon
{
    private readonly HashSet<string> _words;

    private KokoroLexicon(HashSet<string> words) => _words = words;

    /// <summary>The number of words in the dictionary.</summary>
    public int Count => _words.Count;

    /// <summary>Loads the words (the first column of each line) of a sherpa-onnx lexicon file.</summary>
    public static KokoroLexicon Load(string file)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(file, Encoding.UTF8))
        {
            var end = line.IndexOf(' ', StringComparison.Ordinal);
            if (end > 0)
            {
                words.Add(line[..end]);
            }
        }

        return new KokoroLexicon(words);
    }

    /// <summary>A dictionary of the given words (tests).</summary>
    public static KokoroLexicon Of(IEnumerable<string> words) => new(new HashSet<string>(words, StringComparer.Ordinal));

    /// <summary>Whether the dictionary holds <paramref name="word"/> (lowercase).</summary>
    public bool Contains(string word) => _words.Contains(word);

    /// <summary>
    /// The text as it is spoken (hyphenated words split where only their parts are known) and the distinct unknown words, in order of first
    /// appearance (lowercase).
    /// </summary>
    public string Prepare(string text, out IReadOnlyList<string> unknown)
    {
        ArgumentNullException.ThrowIfNull(text);
        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var prepared = WordPattern().Replace(text, match =>
        {
            var word = Normalize(match.Value);
            if (word.Length == 0 || _words.Contains(word))
            {
                return match.Value;
            }

            if (word.Contains('-', StringComparison.Ordinal))
            {
                var parts = word.Split('-', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts.Where(p => !_words.Contains(p)))
                {
                    if (seen.Add(part))
                    {
                        missing.Add(part);
                    }
                }

                return string.Join(' ', match.Value.Split('-', StringSplitOptions.RemoveEmptyEntries));
            }

            if (seen.Add(word))
            {
                missing.Add(word);
            }

            return match.Value;
        });
        unknown = missing;
        return prepared;
    }

    private static string Normalize(string token) =>
        token.Replace('’', '\'').ToLowerInvariant().Trim('\'', '-');

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’\-]*", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
}
