namespace TMConcept.Core.Duplicates;

/// <summary>Concept embeddings (the meaning check).</summary>
public interface IEmbeddingStore
{
    /// <summary>Vectors of a profile made by a model.</summary>
    Task<IReadOnlyList<ConceptEmbedding>> ListAsync(Guid profileId, string model, CancellationToken cancellationToken);

    /// <summary>Adds or replaces vectors.</summary>
    Task SaveAsync(IReadOnlyList<ConceptEmbedding> embeddings, CancellationToken cancellationToken);
}
