using System.Text;

namespace TMConcept.Core.Subtitles;

/// <summary>
/// Subtitles cut by the breath of the speech (0.1.48, F6; rules and numbers of <see cref="CaptionTiming"/>), used only when the words' real
/// times are known (<see cref="WordAligner"/>). A new group starts after a sentence end or a pause of <see cref="CaptionTiming.PauseMs"/>
/// (always), and, once the group has <see cref="CaptionTiming.MinWords"/> words and <see cref="CaptionTiming.MinMs"/>, after a comma with
/// <see cref="CaptionTiming.CommaPauseMs"/> of silence, before a conjunction, at <see cref="CaptionTiming.MaxWords"/> words or past
/// <see cref="CaptionTiming.MaxMs"/>; a word that would need a third line starts a new group too. A group appears
/// <see cref="CaptionTiming.LeadMs"/> before its first word and ends at the earlier of <see cref="CaptionTiming.GapMs"/> before the next group
/// and <see cref="CaptionTiming.TailMs"/> after its last word (never past <c>endMs</c>).
/// </summary>
public static class PauseGrouper
{
    /// <summary>
    /// The cues of <paramref name="words"/> (in order; <paramref name="language"/> chooses the conjunctions), each at most two lines of
    /// <paramref name="lineLength"/> characters, each carrying its words' times (<see cref="SubtitleCue.Words"/>). No cue ends after
    /// <paramref name="endMs"/> or starts before <paramref name="startMs"/>.
    /// </summary>
    public static IReadOnlyList<SubtitleCue> Group(IReadOnlyList<TimedWord> words, string language, int lineLength, long startMs, long endMs)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentOutOfRangeException.ThrowIfLessThan(lineLength, 1);
        var conjunctions = Conjunctions(language);
        var groups = new List<List<TimedWord>>();
        var group = new List<TimedWord>();
        foreach (var word in words)
        {
            if (group.Count > 0 && Breaks(group, word, conjunctions, lineLength))
            {
                groups.Add(group);
                group = [];
            }

            group.Add(word);
        }

        if (group.Count > 0)
        {
            groups.Add(group);
        }

        var cues = new List<SubtitleCue>(groups.Count);
        for (var g = 0; g < groups.Count; g++)
        {
            var start = In(groups[g], startMs);
            var end = groups[g][^1].EndMs + CaptionTiming.TailMs;
            if (g + 1 < groups.Count)
            {
                end = Math.Min(end, In(groups[g + 1], startMs) - CaptionTiming.GapMs);
            }

            end = Math.Max(Math.Min(end, endMs), start + 1);
            cues.Add(new SubtitleCue(start, end, Lines(groups[g].Select(w => w.Text), lineLength)) { Words = groups[g] });
        }

        return cues;
    }

    /// <summary>Whether <paramref name="next"/> starts a new group after <paramref name="group"/>.</summary>
    private static bool Breaks(List<TimedWord> group, TimedWord next, IReadOnlySet<string> conjunctions, int lineLength)
    {
        var last = group[^1];
        var gap = next.StartMs - last.EndMs;
        if (SubtitleBuilder.EndsSentence(last.Text) || gap >= CaptionTiming.PauseMs)
        {
            return true;
        }

        if (Lines(group.Select(w => w.Text).Append(next.Text), lineLength).Count > 2)
        {
            return true;
        }

        if (group.Count < CaptionTiming.MinWords || last.EndMs - group[0].StartMs < CaptionTiming.MinMs)
        {
            return false;
        }

        return (EndsClause(last.Text) && gap >= CaptionTiming.CommaPauseMs)
            || conjunctions.Contains(AlignmentText.Fold(next.Text))
            || group.Count >= CaptionTiming.MaxWords
            || next.EndMs - group[0].StartMs > CaptionTiming.MaxMs;
    }

    /// <summary>
    /// Words that start a new clause (in the form of <see cref="AlignmentText.Fold"/>; the narration's language decides which list): a group
    /// breaks before them once it is long enough. Only the two languages of the app's interface for now; any other language breaks without
    /// them.
    /// </summary>
    public static IReadOnlySet<string> Conjunctions(string language) => (language ?? string.Empty).ToUpperInvariant() switch
    {
        var l when l.StartsWith("TR", StringComparison.Ordinal) => Turkish,
        var l when l.StartsWith("EN", StringComparison.Ordinal) => English,
        _ => None,
    };

    private static readonly IReadOnlySet<string> Turkish = new HashSet<string>(StringComparer.Ordinal)
    {
        "ve", "ama", "fakat", "ancak", "çünkü", "veya", "yoksa", "oysa",
    };

    private static readonly IReadOnlySet<string> English = new HashSet<string>(StringComparer.Ordinal)
    {
        "and", "but", "or", "so", "because", "then", "yet", "while", "although",
    };

    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    private static long In(List<TimedWord> group, long startMs) => Math.Max(startMs, group[0].StartMs - CaptionTiming.LeadMs);

    private static bool EndsClause(string word)
    {
        var end = word.TrimEnd('"', '\'', '»', '«', '”', '“', '’', ')', ']', '›');
        return end.Length > 0 && end[^1] == ',';
    }

    /// <summary>The words wrapped into lines of at most <paramref name="lineLength"/> characters (a longer word keeps a line of its own).</summary>
    internal static List<string> Lines(IEnumerable<string> words, int lineLength)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var word in words)
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > lineLength)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            lines.Add(line.ToString());
        }

        return lines;
    }
}
