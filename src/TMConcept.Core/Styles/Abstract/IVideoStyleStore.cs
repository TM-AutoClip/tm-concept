namespace TMConcept.Core.Styles;

/// <summary>
/// The templates ("Şablonlar" in Settings): a global list in the encrypted database, not tied to profiles. Videos never point at a template;
/// each keeps its own copy (<see cref="Videos.VideoOptions.Style"/>), so editing or deleting one never changes a made video.
/// </summary>
public interface IVideoStyleStore
{
    /// <summary>Every template, by name.</summary>
    Task<IReadOnlyList<VideoStyle>> ListAsync(CancellationToken cancellationToken);

    /// <summary>A template; <c>null</c> when it does not exist (deleted).</summary>
    Task<VideoStyle?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Adds or updates (the caller sets <see cref="VideoStyle.UpdatedAtUtc"/>). Throws <see cref="VideoStyleLimitException"/> when a new
    /// template would make more than <see cref="VideoStyleLimits.Max"/>, and <see cref="ArgumentException"/> when
    /// <see cref="VideoStyleRules.Problems"/> finds a problem; nothing is written then.
    /// </summary>
    Task SaveAsync(VideoStyle style, CancellationToken cancellationToken);

    /// <summary>Deletes the template completely (no recycle bin); a missing id is not an error.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
