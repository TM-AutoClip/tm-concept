namespace TMConcept.Core.Documents;

/// <summary>
/// The arithmetic of "read everything" (0.1.24, map-reduce): passages are packed in order into calls that fit a token budget (map: one
/// note per call), then the notes of a document are packed and merged again (reduce) until they fit the document's share of the final
/// budget, so a model with an 8k context reads a 700-page volume in many small calls instead of failing. The same packing plans the estimate
/// (from the documents' stored lengths) and runs the real calls (from the passages' counted tokens), so the call count shown is the one made.
/// </summary>
public static class DocumentDigestPacker
{
    /// <summary>Answer tokens a note is asked to stay within (about 250 words).</summary>
    public const int NoteTokens = 400;

    /// <summary>Tokens the heading line of a passage or a note takes in a digest call.</summary>
    public const int LineOverhead = 12;

    /// <summary>
    /// The groups of consecutive items of <paramref name="sizes"/> whose sum fits <paramref name="budget"/> (an item larger than the budget is a
    /// group of its own; the call then carries what fits of it). Each group is (start, count).
    /// </summary>
    public static IReadOnlyList<(int Start, int Count)> Pack(IReadOnlyList<long> sizes, long budget)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        var groups = new List<(int Start, int Count)>();
        var start = 0;
        var used = 0L;
        for (var i = 0; i < sizes.Count; i++)
        {
            var size = sizes[i] + LineOverhead;
            if (i > start && used + size > budget)
            {
                groups.Add((start, i - start));
                start = i;
                used = 0;
            }

            used += size;
        }

        if (sizes.Count > start)
        {
            groups.Add((start, sizes.Count - start));
        }

        return groups;
    }

    /// <summary>
    /// How many merge calls bring <paramref name="notes"/> notes of a document down to <paramref name="share"/> tokens with calls of
    /// <paramref name="budget"/> tokens, how many notes stay, and how many notes the merge calls read in all.
    /// </summary>
    public static (int Calls, int Notes, long Read) Reduce(int notes, long budget, long share)
    {
        var perCall = (int)Math.Max(2, budget / (NoteTokens + LineOverhead));
        var calls = 0;
        var read = 0L;
        while (notes > 1 && (long)notes * NoteTokens > share)
        {
            var merged = (notes + perCall - 1) / perCall;
            calls += merged;
            read += notes;
            notes = merged;
        }

        return (calls, Math.Max(notes, 0), read);
    }

    /// <summary>The map calls of a document of <paramref name="passages"/> passages and <paramref name="tokens"/> tokens (passages of equal length assumed).</summary>
    public static int MapCalls(int passages, long tokens, long budget)
    {
        if (passages <= 0 || tokens <= 0)
        {
            return 0;
        }

        var average = Math.Max(1, tokens / passages);
        var perCall = (int)Math.Max(1, budget / (average + LineOverhead));
        return (passages + perCall - 1) / perCall;
    }
}
