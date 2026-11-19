namespace TMConcept.Core.Profiles;

/// <summary>Profiles (channels).</summary>
public interface IProfileStore
{
    /// <summary>Every profile, by name.</summary>
    Task<IReadOnlyList<Profile>> ListAsync(CancellationToken cancellationToken);

    /// <summary>A profile; <c>null</c> when it does not exist.</summary>
    Task<Profile?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Adds or updates.</summary>
    Task SaveAsync(Profile profile, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the profile permanently with its concepts, queue, embeddings and video records. Files on disk are not touched
    /// (<see cref="Deletion.ProfileDeletion"/> deletes them first).
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
