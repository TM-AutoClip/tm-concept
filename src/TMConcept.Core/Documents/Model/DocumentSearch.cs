namespace TMConcept.Core.Documents;

/// <summary>
/// What one search over a profile's passages looks for: the query (empty = no subject: the passages are spread over the documents), the
/// passages to choose from, their stored vectors by passage id (empty = keyword search only), the query's vector (null = none) and the
/// token room (<see cref="DocumentBudget"/>). <see cref="Used"/>: passages earlier batches of the run already sent; they come after every
/// other passage that fits, so the next batch reaches further into the documents. <see cref="Phase"/> (0–1): where the spread starts in
/// each document, so another press without a subject starts elsewhere.
/// </summary>
public sealed record DocumentSearch(string Query, IReadOnlyList<DocumentChunk> Chunks, IReadOnlyDictionary<Guid, float[]> Vectors, float[]? QueryVector,
    int TokenBudget)
{
    /// <summary>The passages already sent by this run (none = empty).</summary>
    public IReadOnlySet<Guid> Used { get; init; } = new HashSet<Guid>();

    /// <summary>Where the spread starts in each document (0 = at its start; kept within 0–1).</summary>
    public double Phase { get; init; }
}
