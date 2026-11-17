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
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.VideoRatings.AsNoTracking().Where(r => r.ProfileId == profileId).OrderByDescending(r => r.RatedAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(VideoRating rating, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rating);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.UpsertAsync(rating, r => r.Id == rating.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.VideoRatings.Where(r => r.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
