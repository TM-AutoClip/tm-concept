namespace TMConcept.Core.Subtitles;

/// <summary>
/// Places the script's words on the heard words' times (0.1.48, F5; the method of ReelMimic's align_lyrics.py, MIT, written anew): the text
/// is always the script's, the recognition is only a ruler. Both texts are folded (<see cref="AlignmentText"/>) and matched letter by letter
/// with a longest common subsequence, which keeps the order; a script letter takes the time of the heard letter it matched (a heard word's
/// time shared evenly by its letters). A word with at least half of its letters matched takes the time of those letters; any other word
/// (skipped by the voice, misheard, a number read out) is placed between its timed neighbours by its length and marked
/// <see cref="TimedWord.Interpolated"/>. Each sentence's <see cref="AlignedSentence.MatchRatio"/> is its share of matched letters.
/// </summary>
public static class WordAligner
{
    /// <summary>
    /// The largest letter table compared at once (script letters × heard letters, one byte each): a part of about two minutes of speech.
    /// A larger one is not aligned (the caller keeps the estimated times).
    /// </summary>
    public const long MaxCells = 16_000_000;

    private static readonly char[] Separators = [' ', '\n', '\r', '\t'];

    /// <summary>
    /// The sentences of <paramref name="text"/> with their words placed between <paramref name="fromMs"/> and <paramref name="toMs"/> on the
    /// times of <paramref name="heard"/> (already limited to that part, in the same time base). Null when the part is too long to compare
    /// (<see cref="MaxCells"/>). A text without words gives no sentence.
    /// </summary>
    public static IReadOnlyList<AlignedSentence>? Align(string text, IReadOnlyList<RecognizedWord> heard, long fromMs, long toMs)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(heard);
        var words = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [];
        }

        toMs = Math.Max(toMs, fromMs + 1);
        var folded = words.Select(AlignmentText.Fold).ToArray();
        var script = new List<(char Letter, int Word)>();
        for (var w = 0; w < folded.Length; w++)
        {
            var word = w;
            script.AddRange(folded[w].Select(c => (c, word)));
        }

        var said = new List<(char Letter, long Start, long End)>();
        foreach (var word in heard)
        {
            var letters = AlignmentText.Fold(word.Text);
            var length = Math.Max(1, word.EndMs - word.StartMs);
            for (var k = 0; k < letters.Length; k++)
            {
                said.Add((letters[k], word.StartMs + (length * k / letters.Length), word.StartMs + (length * (k + 1) / letters.Length)));
            }
        }

        if ((long)script.Count * said.Count > MaxCells)
        {
            return null;
        }

        var matched = new int[words.Length];
        var starts = Enumerable.Repeat(long.MaxValue, words.Length).ToArray();
        var ends = Enumerable.Repeat(long.MinValue, words.Length).ToArray();
        foreach (var (i, j) in Pairs(script.Select(s => s.Letter).ToArray(), said.Select(s => s.Letter).ToArray()))
        {
            var w = script[i].Word;
            matched[w]++;
            starts[w] = Math.Min(starts[w], said[j].Start);
            ends[w] = Math.Max(ends[w], said[j].End);
        }

        var timed = new (long Start, long End)?[words.Length];
        for (var w = 0; w < words.Length; w++)
        {
            if (folded[w].Length > 0 && matched[w] * 2 >= folded[w].Length)
            {
                var start = Math.Clamp(starts[w], fromMs, toMs);
                timed[w] = (start, Math.Clamp(Math.Max(ends[w], start + 1), fromMs, toMs));
            }
        }

        var placed = Place(words, timed, fromMs, toMs);
        return Sentences(words, folded, matched, placed);
    }

    /// <summary>The matched letter pairs (script index, heard index) of a longest common subsequence, in order.</summary>
    internal static IReadOnlyList<(int Script, int Heard)> Pairs(char[] a, char[] b)
    {
        var n = a.Length;
        var m = b.Length;
        if (n == 0 || m == 0)
        {
            return [];
        }

        // The lengths of the common subsequences of the suffixes, row by row from the end, and each cell's way for the walk forward
        // (1 = match, 2 = skip the script letter, 3 = skip the heard letter). Walking forward, two equal letters are always matched at once
        // (that never shortens the result), so a heard word takes the earliest script letters that fit: "dawn" heard after "at" matches the
        // script's "dawn", not letters of the later "walls were silent".
        var way = new byte[(long)n * m];
        var below = new int[m + 1];
        var row = new int[m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            row[m] = 0;
            for (var j = m - 1; j >= 0; j--)
            {
                byte step;
                if (a[i] == b[j])
                {
                    row[j] = below[j + 1] + 1;
                    step = 1;
                }
                else if (below[j] >= row[j + 1])
                {
                    row[j] = below[j];
                    step = 2;
                }
                else
                {
                    row[j] = row[j + 1];
                    step = 3;
                }

                way[((long)i * m) + j] = step;
            }

            (below, row) = (row, below);
        }

        var pairs = new List<(int, int)>();
        var x = 0;
        var y = 0;
        while (x < n && y < m)
        {
            switch (way[((long)x * m) + y])
            {
                case 1:
                    pairs.Add((x, y));
                    x++;
                    y++;
                    break;
                case 2:
                    x++;
                    break;
                default:
                    y++;
                    break;
            }
        }

        return pairs;
    }

    /// <summary>
    /// Every word's time: a timed word keeps its own (never before the previous word's start, so the order holds); a run of untimed words
    /// shares the gap between its timed neighbours (the part's start and end at the edges) by length.
    /// </summary>
    private static List<TimedWord> Place(string[] words, (long Start, long End)?[] timed, long fromMs, long toMs)
    {
        var placed = new List<TimedWord>(words.Length);
        var w = 0;
        var last = fromMs;
        while (w < words.Length)
        {
            if (timed[w] is { } own)
            {
                var start = Math.Max(own.Start, last);
                placed.Add(new TimedWord(words[w], start, Math.Max(own.End, start + 1)));
                last = start;
                w++;
                continue;
            }

            var first = w;
            while (w < words.Length && timed[w] is null)
            {
                w++;
            }

            var from = placed.Count > 0 ? placed[^1].EndMs : fromMs;
            var to = w < words.Length ? Math.Max(timed[w]!.Value.Start, from) : Math.Max(toMs, from);
            var weights = Enumerable.Range(first, w - first).Select(k => words[k].Count(char.IsLetterOrDigit) + 1.0).ToList();
            var total = weights.Sum();
            var at = 0.0;
            for (var k = first; k < w; k++)
            {
                var start = from + (long)Math.Round((to - from) * at / total);
                at += weights[k - first];
                var end = from + (long)Math.Round((to - from) * at / total);
                placed.Add(new TimedWord(words[k], start, Math.Max(end, start + 1), Interpolated: true));
            }

            last = placed[^1].StartMs;
        }

        return placed;
    }

    private static List<AlignedSentence> Sentences(string[] words, string[] folded, int[] matched, List<TimedWord> placed)
    {
        var sentences = new List<AlignedSentence>();
        var first = 0;
        for (var w = 0; w < words.Length; w++)
        {
            if (w == words.Length - 1 || SubtitleBuilder.EndsSentence(words[w]))
            {
                var letters = 0;
                var hit = 0;
                for (var k = first; k <= w; k++)
                {
                    letters += folded[k].Length;
                    hit += Math.Min(matched[k], folded[k].Length);
                }

                sentences.Add(new AlignedSentence(string.Join(' ', words[first..(w + 1)]), placed.GetRange(first, w - first + 1),
                    letters == 0 ? 1.0 : hit / (double)letters));
                first = w + 1;
            }
        }

        return sentences;
    }
}
