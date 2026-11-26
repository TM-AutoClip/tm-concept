using TMConcept.Core.Text;

namespace TMConcept.Core.Music;

/// <summary>
/// Reads what a prompt asks of a composed piece (0.1.55, "Program composes"; pure): the prompt is folded (<see cref="TurkishFold"/>) and its words
/// are looked up in <see cref="ScoreWords"/>. The mood and the style are the most named ones (the earlier one on a tie; the defaults without
/// one). A world or medieval word next to another style only colours the scale ("antik Mısır'da disko" = electronic instruments in an eastern
/// scale); alone it is the style too. The tempo is the one asked for (a reference sound's, the video's), else a slow or fast word, else the mood's.
/// </summary>
public static class ScorePromptReader
{
    /// <summary>What <paramref name="prompt"/> asks for; <paramref name="bpm"/> = a tempo given apart from the words (null = none).</summary>
    public static ScoreTraits Read(string prompt, int? bpm)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var words = TurkishFold.Fold(prompt).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var moods = Ranked(words, ScoreWords.Moods);
        var mood = moods.Count > 0 ? moods[0] : ScoreMoods.Default;
        var styles = Ranked(words, ScoreWords.Styles);
        var flavor = styles.Where(ScoreStyles.Flavors.Contains).Select(s => (ScoreStyle?)s).FirstOrDefault();
        var style = styles.Where(s => !ScoreStyles.Flavors.Contains(s)).Select(s => (ScoreStyle?)s).FirstOrDefault() ?? flavor ?? ScoreStyles.Default;
        var tempo = bpm ?? (Any(words, ScoreWords.Slow) ? ScoreRules.SlowBpm : Any(words, ScoreWords.Fast) ? ScoreRules.FastBpm : ScoreMoods.All[mood].Bpm);
        return new ScoreTraits(mood, style, flavor, Math.Clamp(tempo, ScoreRules.MinBpm, ScoreRules.MaxBpm));
    }

    /// <summary>Whether <paramref name="word"/> of a folded prompt is <paramref name="known"/> (a whole short word, or a long word's start).</summary>
    public static bool Matches(string word, string known)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(known);
        return known.Length <= ScoreWords.WholeWordLength
            ? string.Equals(word, known, StringComparison.Ordinal)
            : word.StartsWith(known, StringComparison.Ordinal);
    }

    /// <summary>The tags named in <paramref name="words"/>, the most named first (the earliest on a tie).</summary>
    private static List<TTag> Ranked<TTag>(IReadOnlyList<string> words, IReadOnlyDictionary<TTag, IReadOnlyList<string>> table)
        where TTag : struct, Enum =>
        words.Select((w, i) => (Index: i, Tag: table.Where(t => t.Value.Any(k => Matches(w, k))).Select(t => (TTag?)t.Key).FirstOrDefault()))
            .Where(p => p.Tag is not null)
            .GroupBy(p => p.Tag!.Value)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Min(p => p.Index))
            .Select(g => g.Key)
            .ToList();

    private static bool Any(IReadOnlyList<string> words, IReadOnlyList<string> known) => words.Any(w => known.Any(k => Matches(w, k)));
}
