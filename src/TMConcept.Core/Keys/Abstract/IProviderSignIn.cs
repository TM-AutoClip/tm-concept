namespace TMConcept.Core.Keys;

/// <summary>
/// A provider that can be used through a sign-in instead of an API key (0.1.47: Google Cloud). The step model resolver counts a sign-in as
/// the provider's key and prefers it to an API key of the same provider.
/// </summary>
public interface IProviderSignIn
{
    /// <summary>Provider code.</summary>
    string Provider { get; }

    /// <summary>The label a run keeps for the sign-in (instead of a key's label).</summary>
    string Label { get; }

    /// <summary>Whether the user is signed in now (a saved sign-in; a refresh token that expired shows only at its first use).</summary>
    Task<bool> IsSignedInAsync(CancellationToken cancellationToken);
}
