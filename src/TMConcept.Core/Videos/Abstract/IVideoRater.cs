namespace TMConcept.Core.Videos;

/// <summary>Rates made videos: like, dislike, or pressing the same one again to clear it.</summary>
public interface IVideoRater
{
    /// <summary>Every rating of a profile, newest first (to show them on the pages: <see cref="VideoRater.Find"/>).</summary>
    Task<IReadOnlyList<VideoRating>> ListAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>
    /// Gives <paramref name="video"/> (and every version of it) <paramref name="vote"/>; when it already has that vote, the rating is cleared.
    /// Returns the vote it has afterwards (null = none).
    /// </summary>
    Task<VideoVote?> RateAsync(VideoRecord video, VideoVote vote, CancellationToken cancellationToken);
}
