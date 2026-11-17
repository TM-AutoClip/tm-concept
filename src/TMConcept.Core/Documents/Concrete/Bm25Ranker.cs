using TMConcept.Core.Text;

namespace TMConcept.Core.Documents;

/// <summary>
/// The keyword search: Okapi BM25 (k1 = 1.2, b = 0.75) over the words of the passages, folded like <see cref="TurkishFold"/> (case and
/// Turkish letters do not matter; one-letter words are skipped). A word also matches by its first <see cref="StemLength"/> letters at half
/// the weight, so a suffixed form finds its stem ("Osmanlıların" finds "Osmanlı", "caravans" finds "caravan"; Turkish adds suffixes, and
/// there is no stemmer for every language). Always available, needs no model; the fallback when the meaning model is not installed.
/// </summary>
public sealed class Bm25Ranker : PassageRankerBase
{
    /// <summary>Letters of a word's stem match (longer words only; a shorter word matches whole).</summary>
    public const int StemLength = 5;

    private const double K1 = 1.2;
    private const double B = 0.75;
    private const double StemWeight = 0.5;

    /// <summary>The folded words of a text.</summary>
    public static IReadOnlyList<string> Words(string? text) =>
        TurkishFold.Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1).ToList();

    /// <summary>The stem of a folded word: its first <see cref="StemLength"/> letters; null for a word that is not longer.</summary>
    public static string? Stem(string word) => word is { Length: > StemLength } ? word[..StemLength] : null;

    /// <inheritdoc />
    protected override IEnumerable<RankedChunk> Score(DocumentSearch search)
    {
        var queryWords = Words(search.Query).Distinct(StringComparer.Ordinal).ToList();
        var passages = search.Chunks.Select(c => Words(c.Heading + " " + c.Text)).ToList();
        var exact = Score(queryWords, passages);
        var stems = Score(queryWords.Select(Stem).OfType<string>().Distinct(StringComparer.Ordinal).ToList(),
            passages.Select(p => (IReadOnlyList<string>)p.Select(w => Stem(w) ?? w).ToList()).ToList());
        for (var i = 0; i < search.Chunks.Count; i++)
        {
            yield return new RankedChunk(search.Chunks[i], exact[i] + (StemWeight * stems[i]));
        }
    }

    /// <summary>The BM25 score of every passage (<paramref name="documents"/>: its words) for <paramref name="query"/>.</summary>
    private static double[] Score(IReadOnlyList<string> query, IReadOnlyList<IReadOnlyList<string>> documents)
    {
        var scores = new double[documents.Count];
        if (query.Count == 0 || documents.Count == 0)
        {
            return scores;
        }

        var average = Math.Max(1, documents.Average(d => d.Count));
        var sets = documents.Select(d => d.ToHashSet(StringComparer.Ordinal)).ToList();
        var frequency = query.ToDictionary(q => q, q => sets.Count(d => d.Contains(q)), StringComparer.Ordinal);
        var n = documents.Count;
        for (var i = 0; i < n; i++)
        {
            var words = documents[i];
            var counts = words.GroupBy(w => w, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var term in query)
            {
                if (!counts.TryGetValue(term, out var tf))
                {
                    continue;
                }

                var df = frequency[term];
                var idf = Math.Log(1 + ((n - df + 0.5) / (df + 0.5)));
                scores[i] += idf * (tf * (K1 + 1)) / (tf + (K1 * (1 - B + (B * words.Count / average))));
            }
        }

        return scores;
    }
}
