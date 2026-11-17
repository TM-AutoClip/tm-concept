using TMConcept.Core.Documents;

namespace TMConcept.Tests.Core;

/// <summary>An <see cref="IDocumentStore"/> in memory, for the sources and the library.</summary>
internal sealed class MemoryDocumentStore : IDocumentStore
{
    public List<SourceDocument> Documents { get; } = [];

    public List<DocumentChunk> Chunks { get; } = [];

    public List<DocumentChunkVector> Vectors { get; } = [];

    /// <summary>A ready document of <paramref name="profileId"/> with one passage per text.</summary>
    public SourceDocument Add(Guid profileId, string title, params string[] passages)
    {
        var document = new SourceDocument
        {
            ProfileId = profileId, Title = title, Chunks = passages.Length, Characters = passages.Sum(p => p.Length), Status = DocumentStatus.Ready, Sha256 = title,
        };
        Documents.Add(document);
        Chunks.AddRange(passages.Select((p, i) => new DocumentChunk { DocumentId = document.Id, ProfileId = profileId, Ordinal = i, Text = p }));
        return document;
    }

    public Task<IReadOnlyList<SourceDocument>> ListAsync(Guid profileId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SourceDocument>>(Documents.Where(d => d.ProfileId == profileId).ToList());

    public Task<IReadOnlyList<SourceDocument>> ListAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SourceDocument>>(Documents.ToList());

    public Task<SourceDocument?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Documents.FirstOrDefault(d => d.Id == id));

    public Task<SourceDocument?> FindByContentAsync(Guid profileId, string sha256, CancellationToken cancellationToken) =>
        Task.FromResult(Documents.FirstOrDefault(d => d.ProfileId == profileId && d.Sha256 == sha256));

    public Task AddAsync(SourceDocument document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        Documents.Add(document);
        Chunks.AddRange(chunks);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var chunks = Chunks.Where(c => c.DocumentId == id).Select(c => c.Id).ToHashSet();
        Vectors.RemoveAll(v => chunks.Contains(v.ChunkId));
        Chunks.RemoveAll(c => c.DocumentId == id);
        Documents.RemoveAll(d => d.Id == id);
        return Task.CompletedTask;
    }

    /// <summary>How many times passages were read (an estimate must read none).</summary>
    public int ChunkReads { get; private set; }

    public Task<IReadOnlyList<DocumentChunk>> ChunksAsync(Guid profileId, IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken)
    {
        ChunkReads++;
        return Task.FromResult<IReadOnlyList<DocumentChunk>>(Chunks.Where(c => c.ProfileId == profileId && documentIds.Contains(c.DocumentId)).ToList());
    }

    public Task<IReadOnlyList<DocumentChunkVector>> VectorsAsync(Guid profileId, string model, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentChunkVector>>(Vectors.Where(v => v.ProfileId == profileId && v.Model == model).ToList());

    public Task<IReadOnlyList<DocumentChunk>> ChunksWithoutVectorAsync(string model, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentChunk>>(Chunks.Where(c => !Vectors.Any(v => v.ChunkId == c.Id && v.Model == model)).ToList());

    public Task SaveVectorsAsync(IReadOnlyList<DocumentChunkVector> vectors, CancellationToken cancellationToken)
    {
        Vectors.RemoveAll(v => vectors.Any(n => n.ChunkId == v.ChunkId && n.Model == v.Model));
        Vectors.AddRange(vectors);
        return Task.CompletedTask;
    }
}
