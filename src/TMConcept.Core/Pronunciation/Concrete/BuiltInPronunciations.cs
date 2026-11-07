using System.Collections.Concurrent;
using System.Text;

namespace TMConcept.Core.Pronunciation;

/// <summary>
/// The pronunciations that come with the app (Pronunciation/Files/&lt;language&gt;.txt, embedded; "word=spoken" lines, "#" comments). Only
/// Turkish has a list so far: the voices read most foreign names of other languages well enough, and the user adds their own.
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
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (line.TrimStart().StartsWith('#') || eq <= 0 || line[(eq + 1)..].Trim().Length == 0)
            {
                continue;
            }

            rules.Add(new PronunciationRule(line[..eq].Trim(), line[(eq + 1)..].Trim(), null));
        }

        return rules;
    }
}
