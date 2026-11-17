namespace TMConcept.Core.Documents;

/// <summary>The documents of the profiles, their passages and the passages' vectors (the encrypted database).</summary>
public interface IDocumentStore
{
    /// <summary>The documents of a profile, the newest first.</summary>
    Task<IReadOnlyList<SourceDocument>> ListAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>Every document of every profile (the storage manager).</summary>
    Task<IReadOnlyList<SourceDocument>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>One document; null when it is not there.</summary>
    Task<SourceDocument?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The profile's document with this content (SHA-256 of the copy); null when there is none.</summary>
    Task<SourceDocument?> FindByContentAsync(Guid profileId, string sha256, CancellationToken cancellationToken);

    /// <summary>Saves a new document with its passages (both or neither).</summary>
    Task AddAsync(SourceDocument document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken);

    /// <summary>Deletes a document with its passages and their vectors.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The passages of <paramref name="documentIds"/> of the profile, in document and passage order.</summary>
    Task<IReadOnlyList<DocumentChunk>> ChunksAsync(Guid profileId, IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken);

    /// <summary>The stored vectors of <paramref name="model"/> of the profile's passages.</summary>
    Task<IReadOnlyList<DocumentChunkVector>> VectorsAsync(Guid profileId, string model, CancellationToken cancellationToken);

    /// <summary>The passages of every profile that have no vector of <paramref name="model"/> yet.</summary>
    Task<IReadOnlyList<DocumentChunk>> ChunksWithoutVectorAsync(string model, CancellationToken cancellationToken);

    /// <summary>Saves vectors (a vector of the same passage and model is replaced).</summary>
    Task SaveVectorsAsync(IReadOnlyList<DocumentChunkVector> vectors, CancellationToken cancellationToken);
}
