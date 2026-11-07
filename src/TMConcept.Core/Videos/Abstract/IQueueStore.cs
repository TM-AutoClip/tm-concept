namespace TMConcept.Core.Videos;

/// <summary>The production queue ("to be produced").</summary>
public interface IQueueStore
{
    /// <summary>Entries of a profile in order.</summary>
    Task<IReadOnlyList<QueueItem>> ListAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>Adds at the end.</summary>
    Task AddAsync(QueueItem item, CancellationToken cancellationToken);

    /// <summary>Removes an entry.</summary>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Keeps the failure of a try: its code and detail, and adds what it paid to <see cref="QueueItem.FailedUsd"/>.</summary>
    Task RecordFailureAsync(Guid id, string code, string detail, decimal spentUsd, CancellationToken cancellationToken);

    /// <summary>Saves a new order (ids in order).</summary>
    Task ReorderAsync(Guid profileId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken);
}
