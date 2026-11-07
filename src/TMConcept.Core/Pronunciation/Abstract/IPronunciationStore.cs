namespace TMConcept.Core.Pronunciation;

/// <summary>The user's pronunciation dictionary (in the encrypted database; a backup holds it with the settings).</summary>
public interface IPronunciationStore
{
    /// <summary>The user's words of a language, oldest first.</summary>
    Task<IReadOnlyList<PronunciationEntry>> ListAsync(string language, CancellationToken cancellationToken);

    /// <summary>Adds a word, or replaces the entry of the same id.</summary>
    Task SaveAsync(PronunciationEntry entry, CancellationToken cancellationToken);

    /// <summary>Removes a word.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
