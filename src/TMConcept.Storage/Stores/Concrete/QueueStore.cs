using Microsoft.EntityFrameworkCore;
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
        return await db.QueueItems.Where(q => q.ProfileId == profileId).OrderBy(q => q.Position).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(QueueItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var profileId = item.ProfileId;
        var last = await db.QueueItems.Where(q => q.ProfileId == profileId).MaxAsync(q => (int?)q.Position, cancellationToken).ConfigureAwait(false);
        item.Position = (last ?? -1) + 1;
        db.QueueItems.Add(item);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.QueueItems.Where(q => q.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReorderAsync(Guid profileId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var items = await db.QueueItems.AsTracking().Where(q => q.ProfileId == profileId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var order = ids.ToList();
        foreach (var item in items)
        {
            var index = order.IndexOf(item.Id);
            item.Position = index >= 0 ? index : ids.Count + item.Position;
        }

        // One save: the changed positions in one transaction.
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
