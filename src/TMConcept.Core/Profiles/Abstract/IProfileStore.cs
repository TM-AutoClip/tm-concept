namespace TMConcept.Core.Profiles;

/// <summary>Profiles (channels) and their sample images.</summary>
public interface IProfileStore
{
    /// <summary>Every profile, by name.</summary>
    Task<IReadOnlyList<Profile>> ListAsync(CancellationToken cancellationToken);

    /// <summary>A profile; <c>null</c> when it does not exist.</summary>
    Task<Profile?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Adds or updates.</summary>
    Task SaveAsync(Profile profile, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the profile permanently with its concepts, queue, image records, embeddings and video records. Files on disk are not touched.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Sample images of a profile.</summary>
    Task<IReadOnlyList<ProfileImage>> ImagesAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>Adds or updates a sample image record.</summary>
    Task SaveImageAsync(ProfileImage image, CancellationToken cancellationToken);

    /// <summary>Deletes a sample image record.</summary>
    Task DeleteImageAsync(Guid id, CancellationToken cancellationToken);
}
