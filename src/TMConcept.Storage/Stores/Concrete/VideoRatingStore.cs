using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Videos;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IVideoRatingStore"/>: the table <c>video_ratings</c>.</summary>
public sealed class VideoRatingStore(ConceptDatabase database) : IVideoRatingStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<VideoRating>> ListAsync(Guid profileId, CancellationToken cancellationToken)
    {
        return await database.ReadAsync<IReadOnlyList<VideoRating>>(async db =>
        {
            return await db.VideoRatings.AsNoTracking().Where(r => r.ProfileId == profileId).OrderByDescending(r => r.RatedAtUtc)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(VideoRating rating, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rating);
        await database.WriteAsync(async db =>
        {
            await db.UpsertAsync(rating, r => r.Id == rating.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await database.WriteAsync(async db =>
        {
            await db.VideoRatings.Where(r => r.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}
