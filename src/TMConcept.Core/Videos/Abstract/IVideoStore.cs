namespace TMConcept.Core.Videos;

/// <summary>Produced videos and their cost breakdown.</summary>
public interface IVideoStore
{
    /// <summary>Videos of a profile, newest first.</summary>
    Task<IReadOnlyList<VideoRecord>> ListAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>One video.</summary>
    Task<VideoRecord?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Adds a video with its cost lines.</summary>
    Task AddAsync(VideoRecord video, IReadOnlyList<VideoCost> costs, CancellationToken cancellationToken);

    /// <summary>Cost lines of a video.</summary>
    Task<IReadOnlyList<VideoCost>> CostsAsync(Guid videoId, CancellationToken cancellationToken);

    /// <summary>Sets the thumbnail file name of a video (a missing thumbnail was made again).</summary>
    Task SetThumbnailAsync(Guid id, string thumbnailName, CancellationToken cancellationToken);

    /// <summary>Deletes the record (and its cost lines) for good; files are handled by the caller.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
