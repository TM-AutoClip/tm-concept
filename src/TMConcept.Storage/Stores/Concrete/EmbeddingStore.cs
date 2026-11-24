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
        return await database.ReadAsync<IReadOnlyList<ConceptEmbedding>>(async db =>
        {
            return await db.Embeddings.Where(e => e.ProfileId == profileId && e.Model == model).ToListAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(IReadOnlyList<ConceptEmbedding> embeddings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        await database.WriteAsync(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var ids = embeddings.Select(e => e.ConceptId).ToList();
            await db.Embeddings.Where(e => ids.Contains(e.ConceptId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            db.Embeddings.AddRange(embeddings);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}
