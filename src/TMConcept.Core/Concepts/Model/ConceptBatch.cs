namespace TMConcept.Core.Concepts;

/// <summary>
/// The result of one batch (shown at once; already saved). <see cref="TopicsMade"/>: the topics of a <see cref="TopicBrief"/> batch that got
/// a concept (a concept thrown away as a duplicate counts: its topic is already among the concepts; empty for an idea run). The page takes
/// them off its tick list, so after a cut or canceled run the topics left ticked are exactly the ones still to make.
/// </summary>
public sealed record ConceptBatch(int Index, int Batches, IReadOnlyList<Concept> Accepted, int RejectedDuplicates, decimal CostUsd,
    IReadOnlyList<string>? TopicsMade = null)
{
    /// <summary>The topics of the batch that got a concept.</summary>
    public IReadOnlyList<string> TopicsMade { get; init; } = TopicsMade ?? [];
}
