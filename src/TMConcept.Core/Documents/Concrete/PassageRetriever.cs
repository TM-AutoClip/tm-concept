using System.Globalization;

namespace TMConcept.Core.Documents;

/// <summary>
/// Picks the passages of one call. With a query (the subject and the batch's topics): every ranking (<see cref="IPassageRanker"/>: the
/// keyword search always, the meaning search when there are vectors) is fused by reciprocal rank (k = 60), then passages are taken in that
/// order while they fit the token budget, at most <see cref="DocumentLimits.PassagesPerDocumentMax"/> from one document while another still
/// has some. Without a query, or when nothing matched, the passages are spread evenly over each document (start, middle, quarters, …) and
/// over the documents, starting at the search's <see cref="DocumentSearch.Phase"/>. Passages an earlier batch of the run already sent
/// (<see cref="DocumentSearch.Used"/>) come after all the others, so a long run walks through the documents instead of sending the same
/// passages again. A passage costs its heading and its text (<see cref="PassageTokens"/>), and the first passage of a document also its title
/// line. The chosen passages go to the prompt in document and passage order; the documents are numbered by their best passage (the most
/// relevant document is [1]).
/// </summary>
public sealed class PassageRetriever(IEnumerable<IPassageRanker> rankers)
{
    private const double FusionK = 60;
    private readonly IReadOnlyList<IPassageRanker> _rankers = [.. rankers];

    /// <summary>
    /// The grounding of one call: passages of <paramref name="search"/> within its budget, counted with <paramref name="tokens"/>;
    /// <paramref name="documents"/> gives the titles (documents not in it are left out).
    /// </summary>
    public DocumentGrounding Select(DocumentSearch search, IReadOnlyList<SourceDocument> documents, Func<string, long> tokens)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(tokens);
        var order = Fused(search);
        var matched = order.Count > 0;
        if (!matched)
        {
            order = Spread(search.Chunks, search.Phase);
        }

        if (search.Used.Count > 0)
        {
            order = [.. order.Where(c => !search.Used.Contains(c.Id)), .. order.Where(c => search.Used.Contains(c.Id))];
        }

        var titles = documents.ToDictionary(d => d.Id, d => d.Title);
        var chosen = Fill(order, search.TokenBudget, tokens, titles);
        var numbers = chosen.Select(c => c.DocumentId).Distinct().Where(titles.ContainsKey).Select((id, i) => new GroundingDocument(i + 1, id, titles[id])).ToList();
        var byId = numbers.ToDictionary(n => n.Id, n => n.Number);
        var passages = chosen.Where(c => byId.ContainsKey(c.DocumentId)).OrderBy(c => byId[c.DocumentId]).ThenBy(c => c.Ordinal)
            .Select(c => new GroundingPassage(byId[c.DocumentId], Locator(c), c.Text, c.Id)).ToList();
        return new DocumentGrounding(numbers, passages) { Matched = matched };
    }

    /// <summary>Where a passage is: "p. 12 · Trade" (page and heading when known).</summary>
    public static string Locator(DocumentChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var parts = new List<string>();
        if (chunk.Page > 0)
        {
            parts.Add("p. " + chunk.Page.ToString(CultureInfo.InvariantCulture));
        }

        if (chunk.Heading.Length > 0)
        {
            parts.Add(chunk.Heading);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>The passages of every ranking fused by reciprocal rank, best first (empty when no ranking found anything).</summary>
    public IReadOnlyList<DocumentChunk> Fused(DocumentSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        var scores = new Dictionary<Guid, (DocumentChunk Chunk, double Score)>();
        foreach (var ranking in _rankers.Select(r => r.Rank(search)))
        {
            for (var rank = 0; rank < ranking.Count; rank++)
            {
                var chunk = ranking[rank].Chunk;
                var add = 1.0 / (FusionK + rank + 1);
                scores[chunk.Id] = scores.TryGetValue(chunk.Id, out var seen) ? (chunk, seen.Score + add) : (chunk, add);
            }
        }

        var position = search.Chunks.Select((c, i) => (c.Id, i)).ToDictionary(p => p.Id, p => p.i);
        return scores.Values.OrderByDescending(s => s.Score).ThenBy(s => position.GetValueOrDefault(s.Chunk.Id)).Select(s => s.Chunk).ToList();
    }

    /// <summary>
    /// Every passage, spread: each document's passages in the order start, middle, first quarter, third quarter, … (the radical inverse of
    /// base 2), shifted by <paramref name="phase"/> (0.3 = the spread starts at 30 % of each document), the documents taking turns.
    /// </summary>
    public static IReadOnlyList<DocumentChunk> Spread(IReadOnlyList<DocumentChunk> chunks, double phase = 0)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        phase = double.IsFinite(phase) ? phase - Math.Floor(phase) : 0;
        var queues = chunks.GroupBy(c => c.DocumentId).Select(g =>
        {
            var list = g.OrderBy(c => c.Ordinal).ToList();
            var picked = new List<DocumentChunk>();
            var seen = new HashSet<int>();
            for (var k = 0; picked.Count < list.Count; k++)
            {
                var at = (int)Math.Floor(Fraction(RadicalInverse(k) + phase) * list.Count) % list.Count;
                if (seen.Add(at))
                {
                    picked.Add(list[at]);
                }
            }

            return new Queue<DocumentChunk>(picked);
        }).ToList();
        var result = new List<DocumentChunk>(chunks.Count);
        while (queues.Any(q => q.Count > 0))
        {
            foreach (var queue in queues.Where(q => q.Count > 0))
            {
                result.Add(queue.Dequeue());
            }
        }

        return result;
    }

    /// <summary>The base-2 radical inverse of <paramref name="k"/> (0, ½, ¼, ¾, ⅛, …).</summary>
    private static double RadicalInverse(int k)
    {
        var result = 0.0;
        var fraction = 0.5;
        while (k > 0)
        {
            result += (k & 1) * fraction;
            k >>= 1;
            fraction /= 2;
        }

        return result;
    }

    private static double Fraction(double value) => value - Math.Floor(value);

    /// <summary>
    /// Takes passages in <paramref name="order"/> while they fit <paramref name="budget"/>, with the per-document cap first, then without it. A
    /// passage costs its locator and text; the first one of a document also its DOCUMENTS line (<paramref name="titles"/>).
    /// </summary>
    private static List<DocumentChunk> Fill(IReadOnlyList<DocumentChunk> order, int budget, Func<string, long> tokens, IReadOnlyDictionary<Guid, string> titles)
    {
        var chosen = new List<DocumentChunk>();
        var taken = new HashSet<Guid>();
        var used = 0L;
        var perDocument = new Dictionary<Guid, int>();
        var documents = order.Select(c => c.DocumentId).Distinct().Count();
        foreach (var capped in new[] { true, false })
        {
            foreach (var chunk in order)
            {
                if (taken.Contains(chunk.Id))
                {
                    continue;
                }

                var count = perDocument.GetValueOrDefault(chunk.DocumentId);
                if (capped && documents > 1 && count >= DocumentLimits.PassagesPerDocumentMax)
                {
                    continue;
                }

                var size = tokens(Locator(chunk) + " " + chunk.Text) + PassageTokens.LineOverhead;
                if (!perDocument.ContainsKey(chunk.DocumentId))
                {
                    size += tokens(titles.GetValueOrDefault(chunk.DocumentId, string.Empty)) + PassageTokens.DocumentOverhead;
                }

                if (used + size > budget)
                {
                    continue;
                }

                chosen.Add(chunk);
                taken.Add(chunk.Id);
                used += size;
                perDocument[chunk.DocumentId] = count + 1;
            }
        }

        return chosen;
    }
}
