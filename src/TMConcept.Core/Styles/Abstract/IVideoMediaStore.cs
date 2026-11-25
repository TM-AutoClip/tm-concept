namespace TMConcept.Core.Styles;

/// <summary>A profile's background music and reference video (0.1.54, <see cref="VideoMedia"/>), kept in the encrypted database with the profile's model choices.</summary>
public interface IVideoMediaStore
{
    /// <summary>The profile's choice; an empty one (music off, no reference) when none was saved.</summary>
    Task<VideoMedia> GetAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>Saves the profile's choice.</summary>
    Task SaveAsync(Guid profileId, VideoMedia media, CancellationToken cancellationToken);
}
