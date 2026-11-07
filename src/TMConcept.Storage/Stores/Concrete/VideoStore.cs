using Microsoft.EntityFrameworkCore;
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
        return await db.Videos.Where(v => v.ProfileId == profileId).OrderByDescending(v => v.CreatedAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VideoRecord?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.Videos.FirstOrDefaultAsync(v => v.Id == id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(VideoRecord video, IReadOnlyList<VideoCost> costs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(costs);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        db.Videos.Add(video);
        foreach (var cost in costs)
        {
            cost.VideoId = video.Id;
            db.VideoCosts.Add(cost);
        }

        // One save: the video and its cost lines in one transaction.
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VideoCost>> CostsAsync(Guid videoId, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.VideoCosts.Where(c => c.VideoId == videoId).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetThumbnailAsync(Guid id, string thumbnailName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(thumbnailName);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.Videos.Where(v => v.Id == id).ExecuteUpdateAsync(s => s.SetProperty(v => v.ThumbnailName, thumbnailName), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.Videos.Where(v => v.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
