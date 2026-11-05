using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Videos;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IQueueStore"/>.</summary>
public sealed class QueueStore(IDbContextFactory<ConceptDbContext> factory) : IQueueStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<QueueItem>> ListAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.QueueItems.AsNoTracking().Where(q => q.ProfileId == profileId).OrderBy(q => q.Position)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(QueueItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var last = await db.QueueItems.Where(q => q.ProfileId == item.ProfileId).Select(q => (int?)q.Position).MaxAsync(cancellationToken).ConfigureAwait(false);
        item.Position = (last ?? -1) + 1;
        db.QueueItems.Add(item);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.QueueItems.Where(q => q.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReorderAsync(Guid profileId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var items = await db.QueueItems.Where(q => q.ProfileId == profileId).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var item in items)
        {
            var index = ids.ToList().IndexOf(item.Id);
            item.Position = index >= 0 ? index : ids.Count + item.Position;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
