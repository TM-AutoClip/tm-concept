namespace TMConcept.Core.Speech;

/// <summary>The user's voice samples (in the encrypted database; a backup holds them with the settings).</summary>
public interface IVoiceSampleStore
{
    /// <summary>Every sample without its sound, oldest first.</summary>
    Task<IReadOnlyList<VoiceSampleInfo>> ListAsync(CancellationToken cancellationToken);

    /// <summary>A sample with its sound; null when it is gone.</summary>
    Task<VoiceSample?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether a sample of this SHA-256 is kept.</summary>
    Task<bool> HasAsync(string sha256, CancellationToken cancellationToken);

    /// <summary>Adds a sample.</summary>
    Task AddAsync(VoiceSample sample, CancellationToken cancellationToken);

    /// <summary>Removes a sample.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
