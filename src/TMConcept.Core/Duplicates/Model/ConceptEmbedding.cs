namespace TMConcept.Core.Duplicates;

/// <summary>The embedding vector of a concept's title and summary (only when the semantic check is on). Deleted with the concept.</summary>
public sealed class ConceptEmbedding
{
    /// <summary>Concept.</summary>
    public Guid ConceptId { get; set; }

    /// <summary>Profile (for loading a profile's vectors).</summary>
    public Guid ProfileId { get; set; }

    /// <summary>Embedding model id.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Vector length.</summary>
    public int Dimensions { get; set; }

    /// <summary>Vector as little-endian float32 bytes.</summary>
    public byte[] Vector { get; set; } = [];
}
