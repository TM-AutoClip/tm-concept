using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Documents;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary>
/// <see cref="IDocumentStore"/>: the tables <c>documents</c>, <c>document_chunks</c> and <c>document_chunk_vectors</c> of the encrypted database
/// (a document's passages and their vectors are deleted with it; a profile's documents with the profile). A write the database refuses (a full
/// disk, a profile deleted meanwhile) comes up as <see cref="InvalidOperationException"/> with the database's reason, which the documents
/// panel shows; vectors of passages deleted meanwhile (their document was deleted during a vector run) are left out instead of failing.
/// </summary>
public sealed class DocumentStore(ConceptDatabase database) : IDocumentStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SourceDocument>> ListAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var documents = await db.Documents.AsNoTracking().Where(d => d.ProfileId == profileId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return documents.OrderByDescending(d => d.AddedAtUtc).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SourceDocument>> ListAllAsync(CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.Documents.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SourceDocument?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SourceDocument?> FindByContentAsync(Guid profileId, string sha256, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.ProfileId == profileId && d.Sha256 == sha256, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(SourceDocument document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(chunks);
        try
        {
            await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            db.Documents.Add(document);
            db.DocumentChunks.AddRange(chunks);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbUpdateException or SqliteException)
        {
            throw new InvalidOperationException("The document could not be saved: " + (ex.InnerException ?? ex).Message, ex);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var chunks = db.DocumentChunks.Where(c => c.DocumentId == id).Select(c => c.Id);
        await db.DocumentChunkVectors.Where(v => chunks.Contains(v.ChunkId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.DocumentChunks.Where(c => c.DocumentId == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Documents.Where(d => d.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentChunk>> ChunksAsync(Guid profileId, IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        var ids = documentIds.ToList();
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var chunks = await db.DocumentChunks.AsNoTracking().Where(c => c.ProfileId == profileId && ids.Contains(c.DocumentId)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var order = ids.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i);
        return chunks.OrderBy(c => order[c.DocumentId]).ThenBy(c => c.Ordinal).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentChunkVector>> VectorsAsync(Guid profileId, string model, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.DocumentChunkVectors.AsNoTracking().Where(v => v.ProfileId == profileId && v.Model == model).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentChunk>> ChunksWithoutVectorAsync(string model, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var embedded = db.DocumentChunkVectors.Where(v => v.Model == model).Select(v => v.ChunkId);
        return await db.DocumentChunks.AsNoTracking().Where(c => !embedded.Contains(c.Id)).OrderBy(c => c.DocumentId).ThenBy(c => c.Ordinal)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveVectorsAsync(IReadOnlyList<DocumentChunkVector> vectors, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        if (vectors.Count == 0)
        {
            return;
        }

        var ids = vectors.Select(v => v.ChunkId).ToList();
        var model = vectors[0].Model;
        try
        {
            await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var present = (await db.DocumentChunks.Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
            await db.DocumentChunkVectors.Where(v => v.Model == model && ids.Contains(v.ChunkId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            db.DocumentChunkVectors.AddRange(vectors.Where(v => present.Contains(v.ChunkId)));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbUpdateException or SqliteException)
        {
            throw new InvalidOperationException("The passages' vectors could not be saved: " + (ex.InnerException ?? ex).Message, ex);
        }
    }
}
