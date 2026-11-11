namespace TMConcept.Core.Concepts;

/// <summary>
/// The result of one batch (shown at once; already saved). <see cref="TopicsMade"/>: the selected topics of the batch that got a concept
/// (a duplicate thrown away counts; empty without topics), so the page knows them even if a later batch fails unexpectedly.
/// </summary>
public sealed record ConceptBatch(int Index, int Batches, IReadOnlyList<Concept> Accepted, int RejectedDuplicates, decimal CostUsd,
    IReadOnlyList<string>? TopicsMade = null)
{
    /// <summary>The selected topics of the batch that got a concept.</summary>
    public IReadOnlyList<string> TopicsMade { get; init; } = TopicsMade ?? [];
}
