using TMConcept.Core.Keys;

namespace TMConcept.Core.Speech;

/// <summary>An online provider that lists the voices of the user's account (0.1.47: ElevenLabs).</summary>
public interface IAccountVoiceSource
{
    /// <summary>Provider code ("elevenlabs").</summary>
    string Provider { get; }

    /// <summary>Reads every voice of the account the key belongs to (all pages). A failure throws <see cref="Providers.ProviderException"/>.</summary>
    Task<IReadOnlyList<AccountVoice>> FetchAsync(ApiKey key, CancellationToken cancellationToken);
}
