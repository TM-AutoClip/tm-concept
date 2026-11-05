namespace TMConcept.Core.Duplicates;

/// <summary>Concept embeddings and history digests.</summary>
public interface IEmbeddingStore
{
    /// <summary>Vectors of a profile made by a model.</summary>
    Task<IReadOnlyList<ConceptEmbedding>> ListAsync(Guid profileId, string model, CancellationToken cancellationToken);

    /// <summary>Adds or replaces vectors.</summary>
    Task SaveAsync(IReadOnlyList<ConceptEmbedding> embeddings, CancellationToken cancellationToken);

    /// <summary>The digest of a profile; <c>null</c> when none.</summary>
    Task<HistoryDigest?> DigestAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>Saves the digest.</summary>
    Task SaveDigestAsync(HistoryDigest digest, CancellationToken cancellationToken);
}
