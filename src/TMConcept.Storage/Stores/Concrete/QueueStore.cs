using Microsoft.Data.Sqlite;
using TMConcept.Core.Videos;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IQueueStore"/>.</summary>
public sealed class QueueStore(ConceptDatabase database) : IQueueStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<QueueItem>> ListAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await QueueItemTable.Instance.ListAsync(db, "\"ProfileId\" = $profile", cancellationToken, null, "\"Position\"", ("$profile", Sql.Id(profileId)))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(QueueItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await db.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var last = await Sql.ScalarAsync(db, "SELECT MAX(\"Position\") FROM \"queue_items\" WHERE \"ProfileId\" = $profile", cancellationToken, transaction,
            ("$profile", Sql.Id(item.ProfileId))).ConfigureAwait(false);
        item.Position = (int)(last ?? -1) + 1;
        await QueueItemTable.Instance.InsertAsync(db, item, cancellationToken, transaction).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await QueueItemTable.Instance.DeleteAsync(db, "\"Id\" = $id", cancellationToken, null, ("$id", Sql.Id(id))).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReorderAsync(Guid profileId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await db.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var items = await QueueItemTable.Instance.ListAsync(db, "\"ProfileId\" = $profile", cancellationToken, transaction, string.Empty,
            ("$profile", Sql.Id(profileId))).ConfigureAwait(false);
        var order = ids.ToList();
        foreach (var item in items)
        {
            var index = order.IndexOf(item.Id);
            var position = index >= 0 ? index : ids.Count + item.Position;
            await Sql.ExecuteAsync(db, "UPDATE \"queue_items\" SET \"Position\" = $position WHERE \"Id\" = $id", cancellationToken, transaction,
                ("$position", position), ("$id", Sql.Id(item.Id))).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
