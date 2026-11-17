using Microsoft.Extensions.Logging;
using TMConcept.Core.Concepts;

namespace TMConcept.Core.Videos;

/// <summary>
/// <see cref="IVideoRater"/>: one rating per video, shared by all its versions (the developer, 17 Nov 2026). A video is matched by its concept
/// when it has one, otherwise by its folder without the version folder (<see cref="GroupOf"/>). The concept's title and summary are copied
/// into the rating, so it keeps steering the prompts after the concept or the video is deleted. Pressing the vote the video already has
/// clears it. Producing a video never rates it.
/// </summary>
public sealed class VideoRater(IVideoRatingStore ratings, IConceptStore concepts, TimeProvider clock, ILogger<VideoRater> logger) : IVideoRater
{
    /// <inheritdoc />
    public Task<IReadOnlyList<VideoRating>> ListAsync(Guid profileId, CancellationToken cancellationToken) => ratings.ListAsync(profileId, cancellationToken);

    /// <inheritdoc />
    public async Task<VideoVote?> RateAsync(VideoRecord video, VideoVote vote, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(video);
        var existing = Find(await ratings.ListAsync(video.ProfileId, cancellationToken).ConfigureAwait(false), video);
        if (existing?.Vote == vote)
        {
            await ratings.DeleteAsync(existing.Id, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Video rating cleared");
            return null;
        }

        var concept = video.ConceptId is { } conceptId ? await concepts.GetAsync(conceptId, cancellationToken).ConfigureAwait(false) : null;
        var rating = existing ?? new VideoRating { ProfileId = video.ProfileId };
        rating.ConceptId = concept?.Id ?? rating.ConceptId;
        rating.VideoGroup = GroupOf(video.Folder);
        rating.Title = concept?.Title is { Length: > 0 } title ? title : video.Title;
        rating.Summary = concept?.Summary ?? rating.Summary;
        rating.Vote = vote;
        rating.RatedAtUtc = clock.GetUtcNow();
        await ratings.SaveAsync(rating, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Video rated: {Vote}", vote);
        return vote;
    }

    /// <summary>The rating of <paramref name="video"/> among <paramref name="all"/> (same profile): by its concept, else by its folder.</summary>
    public static VideoRating? Find(IEnumerable<VideoRating> all, VideoRecord video)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(video);
        var group = GroupOf(video.Folder);
        var list = all as IReadOnlyCollection<VideoRating> ?? all.ToList();
        return (video.ConceptId is { } conceptId ? list.FirstOrDefault(r => r.ConceptId == conceptId) : null)
            ?? (group.Length > 0 ? list.FirstOrDefault(r => string.Equals(r.VideoGroup, group, StringComparison.Ordinal)) : null);
    }

    /// <summary>
    /// The folder every version of a video shares: <paramref name="folder"/> (relative to the library) without its version folder
    /// ("Byzantium/Greek fire/v2" → "Byzantium/Greek fire"), with "/" separators.
    /// </summary>
    public static string GroupOf(string? folder)
    {
        var parts = (folder ?? string.Empty).Split(['/', '\\'],StringSplitOptions.RemoveEmptyEntries);
        var keep = parts.Length > 1 && VersionFolders.NumberOf(parts[^1]) is not null ? parts.Length - 1 : parts.Length;
        return string.Join('/', parts.Take(keep));
    }
}
