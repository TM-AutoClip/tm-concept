namespace TMConcept.Core.Documents;

/// <summary>
/// What one search over a profile's passages looks for: the query (empty = no subject: the passages are spread over the documents), the
/// passages to choose from, their stored vectors by passage id (empty = keyword search only), the query's vector (null = none) and the
/// token room (<see cref="DocumentBudget"/>).
/// </summary>
public sealed record DocumentSearch(string Query, IReadOnlyList<DocumentChunk> Chunks, IReadOnlyDictionary<Guid, float[]> Vectors, float[]? QueryVector,
    int TokenBudget);
