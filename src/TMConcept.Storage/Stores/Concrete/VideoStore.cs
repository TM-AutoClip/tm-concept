using Microsoft.Data.Sqlite;
using TMConcept.Core.Videos;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IVideoStore"/>.</summary>
public sealed class VideoStore(ConceptDatabase database) : IVideoStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<VideoRecord>> ListAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await VideoTable.Instance.ListAsync(db, "\"ProfileId\" = $profile", cancellationToken, null, "\"CreatedAtUtc\" DESC",
            ("$profile", Sql.Id(profileId))).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VideoRecord?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await VideoTable.Instance.FirstAsync(db, "\"Id\" = $id", cancellationToken, null, ("$id", Sql.Id(id))).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(VideoRecord video, IReadOnlyList<VideoCost> costs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(costs);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await db.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await VideoTable.Instance.InsertAsync(db, video, cancellationToken, transaction).ConfigureAwait(false);
        foreach (var cost in costs)
        {
            cost.VideoId = video.Id;
            await VideoCostTable.Instance.InsertAsync(db, cost, cancellationToken, transaction).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VideoCost>> CostsAsync(Guid videoId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await VideoCostTable.Instance.ListAsync(db, "\"VideoId\" = $video", cancellationToken, null, string.Empty, ("$video", Sql.Id(videoId)))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetThumbnailAsync(Guid id, string thumbnailName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(thumbnailName);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await Sql.ExecuteAsync(db, "UPDATE \"videos\" SET \"ThumbnailName\" = $name WHERE \"Id\" = $id", cancellationToken, null,
            ("$name", thumbnailName), ("$id", Sql.Id(id))).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await VideoTable.Instance.DeleteAsync(db, "\"Id\" = $id", cancellationToken, null, ("$id", Sql.Id(id))).ConfigureAwait(false);
    }
}
