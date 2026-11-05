namespace TMConcept.Core.Concepts;

/// <summary>The result of one batch (shown at once; already saved).</summary>
public sealed record ConceptBatch(int Index, int Batches, IReadOnlyList<Concept> Accepted, int RejectedDuplicates, decimal CostUsd);
