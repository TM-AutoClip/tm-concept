namespace TMConcept.Core.Duplicates;

/// <summary>Vectors and billed tokens.</summary>
public sealed record EmbeddingResult(IReadOnlyList<float[]> Vectors, long Tokens);
