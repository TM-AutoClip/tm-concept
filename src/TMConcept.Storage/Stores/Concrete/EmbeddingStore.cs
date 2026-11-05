using Microsoft.Data.Sqlite;
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
        return await ConceptEmbeddingTable.Instance.ListAsync(db, "\"ProfileId\" = $profile AND \"Model\" = $model", cancellationToken, null, string.Empty,
            ("$profile", Sql.Id(profileId)), ("$model", model)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(IReadOnlyList<ConceptEmbedding> embeddings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await db.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var (clause, parameters) = Sql.In("ConceptId", embeddings.Select(e => (object?)Sql.Id(e.ConceptId)));
        await ConceptEmbeddingTable.Instance.DeleteAsync(db, clause, cancellationToken, transaction, parameters).ConfigureAwait(false);
        foreach (var embedding in embeddings)
        {
            await ConceptEmbeddingTable.Instance.InsertAsync(db, embedding, cancellationToken, transaction).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<HistoryDigest?> DigestAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await HistoryDigestTable.Instance.FirstAsync(db, "\"ProfileId\" = $profile", cancellationToken, null, ("$profile", Sql.Id(profileId)))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveDigestAsync(HistoryDigest digest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(digest);
        digest.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await HistoryDigestTable.Instance.UpsertAsync(db, digest, cancellationToken).ConfigureAwait(false);
    }
}
