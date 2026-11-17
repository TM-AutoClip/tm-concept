namespace TMConcept.Core.Documents;

/// <summary>
/// The meaning search's vector of one passage for one model (table <c>document_chunk_vectors</c>; deleted with its passage). Kept apart
/// from the passage, so another model keeps the passages and only makes its own vectors.
/// </summary>
public sealed class DocumentChunkVector
{
    /// <summary>The passage.</summary>
    public Guid ChunkId { get; set; }

    /// <summary>The model that made it.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>The document's profile.</summary>
    public Guid ProfileId { get; set; }

    /// <summary>Dimensions.</summary>
    public int Dimensions { get; set; }

    /// <summary>The normalized vector, little-endian floats.</summary>
    public byte[] Vector { get; set; } = [];
}
