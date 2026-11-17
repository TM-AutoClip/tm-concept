namespace TMConcept.Core.Videos;

/// <summary>The likes and dislikes of made videos (<see cref="VideoRating"/>), per profile.</summary>
public interface IVideoRatingStore
{
    /// <summary>Every rating of a profile, newest first.</summary>
    Task<IReadOnlyList<VideoRating>> ListAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>Adds the rating, or replaces the stored one with the same id.</summary>
    Task SaveAsync(VideoRating rating, CancellationToken cancellationToken);

    /// <summary>Removes a rating (the video is neutral again).</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
