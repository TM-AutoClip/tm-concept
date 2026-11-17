using TMConcept.Core.Text;

namespace TMConcept.Core.Documents;

/// <summary>
/// The keyword search: Okapi BM25 (k1 = 1.2, b = 0.75) over the words of the passages, folded like <see cref="TurkishFold"/> (case and
/// Turkish letters do not matter; one-letter words are skipped). Always available, needs no model; the fallback when the meaning model is
/// not installed.
/// </summary>
public sealed class Bm25Ranker : PassageRankerBase
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    /// <summary>The folded words of a text.</summary>
    public static IReadOnlyList<string> Words(string? text) =>
        TurkishFold.Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1).ToList();

    /// <inheritdoc />
    protected override IEnumerable<RankedChunk> Score(DocumentSearch search)
    {
        var query = Words(search.Query).Distinct(StringComparer.Ordinal).ToList();
        var documents = search.Chunks.Select(c => Words(c.Heading + " " + c.Text)).ToList();
        var average = Math.Max(1, documents.Average(d => d.Count));
        var frequency = query.ToDictionary(q => q, q => documents.Count(d => d.Contains(q, StringComparer.Ordinal)), StringComparer.Ordinal);
        var n = documents.Count;
        for (var i = 0; i < n; i++)
        {
            var words = documents[i];
            var counts = words.GroupBy(w => w, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            var score = 0.0;
            foreach (var term in query)
            {
                if (!counts.TryGetValue(term, out var tf))
                {
                    continue;
                }

                var df = frequency[term];
                var idf = Math.Log(1 + ((n - df + 0.5) / (df + 0.5)));
                score += idf * (tf * (K1 + 1)) / (tf + (K1 * (1 - B + (B * words.Count / average))));
            }

            yield return new RankedChunk(search.Chunks[i], score);
        }
    }
}
