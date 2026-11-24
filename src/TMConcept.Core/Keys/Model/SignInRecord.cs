namespace TMConcept.Core.Keys;

/// <summary>
/// A sign-in at a provider instead of an API key (0.1.47: "Sign in with Google" for Google Cloud Text-to-Speech, with the user's own
/// desktop OAuth client). The client secret and the refresh token live in the encrypted database next to the keys and travel in a backup
/// only when the keys are ticked; they are never logged. The short-lived access token is kept in memory only.
/// </summary>
public sealed class SignInRecord
{
    /// <summary>Provider code ("google-cloud").</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The OAuth client id (from the user's client file).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The OAuth client secret (from the same file; not a real secret for a desktop client, kept encrypted all the same).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>The project billed for the requests (<c>x-goog-user-project</c>).</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>The refresh token.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Signed in at (UTC).</summary>
    public DateTimeOffset SignedInAtUtc { get; set; }
}
