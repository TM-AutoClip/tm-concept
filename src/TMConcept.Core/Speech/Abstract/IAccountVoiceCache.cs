namespace TMConcept.Core.Speech;

/// <summary>The last account voice list read from each provider (encrypted database; no schema of its own).</summary>
public interface IAccountVoiceCache
{
    /// <summary>The saved list of <paramref name="provider"/>; null when none was saved.</summary>
    Task<IReadOnlyList<AccountVoice>?> GetAsync(string provider, CancellationToken cancellationToken);

    /// <summary>Replaces the saved list of <paramref name="provider"/>.</summary>
    Task SaveAsync(string provider, IReadOnlyList<AccountVoice> voices, CancellationToken cancellationToken);
}
