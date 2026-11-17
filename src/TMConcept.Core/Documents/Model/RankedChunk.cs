namespace TMConcept.Core.Documents;

/// <summary>A passage with its score from one ranking (higher = more relevant).</summary>
public sealed record RankedChunk(DocumentChunk Chunk, double Score);
