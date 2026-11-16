using System.Collections.Concurrent;
using System.Text;

namespace TMConcept.Core.Pronunciation;

/// <summary>
/// The pronunciations that come with the app (Pronunciation/Files/&lt;language&gt;.txt, embedded). One "word=spoken" per line; "#" starts
/// a comment line, "## heading" starts a category (shown in the settings list); "!WORD=spoken" is matched only as written, letter case
/// included (abbreviations such as "!IT", so the English-looking "it" of a normal sentence stays). Only Turkish has a list so far: the
/// voices read most foreign names of other languages well enough, and the user adds their own.
/// </summary>
public static class BuiltInPronunciations
{
    private static readonly ConcurrentDictionary<string, IReadOnlyList<PronunciationRule>> Cache = new(StringComparer.Ordinal);

    /// <summary>The built-in rules of a language (empty when it has none).</summary>
    public static IReadOnlyList<PronunciationRule> Of(string language) => Cache.GetOrAdd(language ?? string.Empty, Load);

    private static IReadOnlyList<PronunciationRule> Load(string language)
    {
        using var stream = typeof(BuiltInPronunciations).Assembly.GetManifestResourceStream($"TMConcept.Pronunciation.{language}.txt");
        if (stream is null)
        {
            return [];
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>The rules of a list's text.</summary>
    public static IReadOnlyList<PronunciationRule> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rules = new List<PronunciationRule>();
        var category = string.Empty;
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("##", StringComparison.Ordinal))
            {
                category = line[2..].Trim();
                continue;
            }

            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (line.StartsWith('#') || eq <= 0 || line[(eq + 1)..].Trim().Length == 0)
            {
                continue;
            }

            var word = line[..eq].Trim();
            var exact = word[0] == PronunciationRule.ExactMark;
            word = exact ? word[1..].Trim() : word;
            if (word.Length > 0)
            {
                rules.Add(new PronunciationRule(word, line[(eq + 1)..].Trim(), null, exact, category));
            }
        }

        return rules;
    }
}
