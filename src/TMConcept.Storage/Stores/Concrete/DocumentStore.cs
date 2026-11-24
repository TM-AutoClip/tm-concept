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
/// 0.1.25, <see cref="IDocumentPageStore"/>: the table <c>document_pages</c>, the kept page texts of the profiles' PDF files; a file's pages are
/// deleted with the last document of that file in the profile (and with the profile).
/// </summary>
public sealed class DocumentStore(ConceptDatabase database) : IDocumentStore, IDocumentPageStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SourceDocument>> ListAsync(Guid profileId, CancellationToken cancellationToken)
    {
        return await database.ReadAsync<IReadOnlyList<SourceDocument>>(async db =>
        {
            var documents = await db.Documents.AsNoTracking().Where(d => d.ProfileId == profileId).ToListAsync(cancellationToken).ConfigureAwait(false);
            return documents.OrderByDescending(d => d.AddedAtUtc).ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SourceDocument>> ListAllAsync(CancellationToken cancellationToken)
    {
        return await database.ReadAsync<IReadOnlyList<SourceDocument>>(async db =>
        {
            return await db.Documents.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SourceDocument?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return await database.ReadAsync<SourceDocument?>(async db =>
        {
            return await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SourceDocument?> FindByContentAsync(Guid profileId, string sha256, CancellationToken cancellationToken)
    {
        return await database.ReadAsync<SourceDocument?>(async db =>
        {
            return await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.ProfileId == profileId && d.Sha256 == sha256, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(SourceDocument document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(chunks);
        try
        {
            await database.WriteAsync(async db =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                db.Documents.Add(document);
                db.DocumentChunks.AddRange(chunks);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbUpdateException or SqliteException)
        {
            throw new InvalidOperationException("The document could not be saved: " + (ex.InnerException ?? ex).Message, ex);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await database.WriteAsync(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var chunks = db.DocumentChunks.Where(c => c.DocumentId == id).Select(c => c.Id);
            var file = await db.Documents.Where(d => d.Id == id).Select(d => new { d.ProfileId, d.FileSha256 }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            await db.DocumentChunkVectors.Where(v => chunks.Contains(v.ChunkId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.DocumentChunks.Where(c => c.DocumentId == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Documents.Where(d => d.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            if (file is { FileSha256.Length: > 0 }
                && !await db.Documents.AnyAsync(d => d.ProfileId == file.ProfileId && d.FileSha256 == file.FileSha256, cancellationToken).ConfigureAwait(false))
            {
                // The last document of this file in the profile: its kept page texts go with it.
                await db.DocumentPages.Where(p => p.ProfileId == file.ProfileId && p.FileSha256 == file.FileSha256).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentChunk>> ChunksAsync(Guid profileId, IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        var ids = documentIds.ToList();
        return await database.ReadAsync<IReadOnlyList<DocumentChunk>>(async db =>
        {
            var chunks = await db.DocumentChunks.AsNoTracking().Where(c => c.ProfileId == profileId && ids.Contains(c.DocumentId)).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var order = ids.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i);
            return chunks.OrderBy(c => order[c.DocumentId]).ThenBy(c => c.Ordinal).ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentChunkVector>> VectorsAsync(Guid profileId, string model, CancellationToken cancellationToken)
    {
        return await database.ReadAsync<IReadOnlyList<DocumentChunkVector>>(async db =>
        {
            return await db.DocumentChunkVectors.AsNoTracking().Where(v => v.ProfileId == profileId && v.Model == model).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentChunk>> ChunksWithoutVectorAsync(string model, CancellationToken cancellationToken)
    {
        return await database.ReadAsync<IReadOnlyList<DocumentChunk>>(async db =>
        {
            var embedded = db.DocumentChunkVectors.Where(v => v.Model == model).Select(v => v.ChunkId);
            return await db.DocumentChunks.AsNoTracking().Where(c => !embedded.Contains(c.Id)).OrderBy(c => c.DocumentId).ThenBy(c => c.Ordinal)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
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
            await database.WriteAsync(async db =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                var present = (await db.DocumentChunks.Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
                await db.DocumentChunkVectors.Where(v => v.Model == model && ids.Contains(v.ChunkId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                db.DocumentChunkVectors.AddRange(vectors.Where(v => present.Contains(v.ChunkId)));
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbUpdateException or SqliteException)
        {
            throw new InvalidOperationException("The passages' vectors could not be saved: " + (ex.InnerException ?? ex).Message, ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentPageText>> PagesAsync(Guid profileId, string fileSha256, int from, int to, CancellationToken cancellationToken)
    {
        return await database.ReadAsync<IReadOnlyList<DocumentPageText>>(async db =>
        {
            return await db.DocumentPages.AsNoTracking().Where(p => p.ProfileId == profileId && p.FileSha256 == fileSha256 && p.Page >= from && p.Page <= to)
                .OrderBy(p => p.Page).ToListAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SavePagesAsync(IReadOnlyList<DocumentPageText> pages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
        {
            return;
        }

        try
        {
            await database.WriteAsync(async db =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                foreach (var file in pages.GroupBy(p => (p.ProfileId, p.FileSha256)))
                {
                    var numbers = file.Select(p => p.Page).ToList();
                    await db.DocumentPages.Where(p => p.ProfileId == file.Key.ProfileId && p.FileSha256 == file.Key.FileSha256 && numbers.Contains(p.Page))
                        .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                }

                db.DocumentPages.AddRange(pages.GroupBy(p => (p.ProfileId, p.FileSha256, p.Page)).Select(g => g.Last()));
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbUpdateException or SqliteException)
        {
            throw new InvalidOperationException("The read pages could not be kept: " + (ex.InnerException ?? ex).Message, ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentPageMark>> MarksAsync(Guid profileId, CancellationToken cancellationToken)
    {
        return await database.ReadAsync<IReadOnlyList<DocumentPageMark>>(async db =>
        {
            return await db.DocumentPages.AsNoTracking().Where(p => p.ProfileId == profileId)
                .Select(p => new DocumentPageMark(p.ProfileId, p.FileSha256, p.Page, p.Text.Length, p.Source)).ToListAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ForgetAsync(Guid profileId, string fileSha256, CancellationToken cancellationToken)
    {
        await database.WriteAsync(async db =>
        {
            await db.DocumentPages.Where(p => p.ProfileId == profileId && p.FileSha256 == fileSha256).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReplaceAsync(SourceDocument document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(chunks);
        try
        {
            await database.WriteAsync(async db =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                var old = db.DocumentChunks.Where(c => c.DocumentId == document.Id).Select(c => c.Id);
                await db.DocumentChunkVectors.Where(v => old.Contains(v.ChunkId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                await db.DocumentChunks.Where(c => c.DocumentId == document.Id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                db.Documents.Update(document);
                db.DocumentChunks.AddRange(chunks);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbUpdateException or SqliteException)
        {
            throw new InvalidOperationException("The document could not be saved: " + (ex.InnerException ?? ex).Message, ex);
        }
    }
}
