namespace TMConcept.Core.Documents;

/// <summary>One way of ranking a profile's passages for a query (by its words, by meaning). The retriever fuses the rankings it gets.</summary>
public interface IPassageRanker
{
    /// <summary>The best passages for <paramref name="search"/>, the most relevant first (empty when this ranking cannot run, for example no vectors).</summary>
    IReadOnlyList<RankedChunk> Rank(DocumentSearch search);
}
