using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Duplicates;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IEmbeddingStore"/>.</summary>
public sealed class EmbeddingStore(ConceptDatabase database) : IEmbeddingStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ConceptEmbedding>> ListAsync(Guid profileId, string model, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.Embeddings.Where(e => e.ProfileId == profileId && e.Model == model).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(IReadOnlyList<ConceptEmbedding> embeddings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var ids = embeddings.Select(e => e.ConceptId).ToList();
        await db.Embeddings.Where(e => ids.Contains(e.ConceptId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        db.Embeddings.AddRange(embeddings);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<HistoryDigest?> DigestAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.HistoryDigests.FirstOrDefaultAsync(d => d.ProfileId == profileId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveDigestAsync(HistoryDigest digest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(digest);
        digest.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.UpsertAsync(digest, d => d.ProfileId == digest.ProfileId, cancellationToken).ConfigureAwait(false);
    }
}
