namespace TMConcept.Core.Documents;

/// <summary>
/// The common part of a ranking: nothing to rank without a query or passages; every passage is scored, only positive scores count, the best
/// <see cref="Top"/> come back, ties in document order.
/// </summary>
public abstract class PassageRankerBase : IPassageRanker
{
    /// <summary>How many passages one ranking gives at most.</summary>
    public const int Top = 40;

    /// <inheritdoc />
    public IReadOnlyList<RankedChunk> Rank(DocumentSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        if (search.Query.Trim().Length == 0 || search.Chunks.Count == 0 || !CanRank(search))
        {
            return [];
        }

        return Score(search).Where(r => r.Score > 0).Select((r, i) => (r, i)).OrderByDescending(p => p.r.Score).ThenBy(p => p.i).Take(Top).Select(p => p.r).ToList();
    }

    /// <summary>Whether this ranking has what it needs (vectors, for example).</summary>
    protected virtual bool CanRank(DocumentSearch search) => true;

    /// <summary>A score for every passage of <paramref name="search"/>, in the passages' order.</summary>
    protected abstract IEnumerable<RankedChunk> Score(DocumentSearch search);
}
