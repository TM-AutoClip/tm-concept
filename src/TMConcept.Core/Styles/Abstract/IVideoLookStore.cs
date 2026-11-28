namespace TMConcept.Core.Styles;

/// <summary>A profile's look (28 Nov 2026, <see cref="VideoLook"/>), kept in the encrypted database with the profile's model choices.</summary>
public interface IVideoLookStore
{
    /// <summary>The profile's look; the default one (the AI chooses) when none was saved.</summary>
    Task<VideoLook> GetAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>Saves the profile's look.</summary>
    Task SaveAsync(Guid profileId, VideoLook value, CancellationToken cancellationToken);
}
