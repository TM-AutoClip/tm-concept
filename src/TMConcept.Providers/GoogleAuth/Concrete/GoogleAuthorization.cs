using System.Text;

namespace TMConcept.Providers.GoogleAuth;

/// <summary>
/// Google's OAuth 2.0 for desktop apps (the loopback flow; Google's "OAuth 2.0 for iOS &amp; desktop apps" page, read on 24 Nov 2026: the
/// manual copy-paste flow is no longer supported). The authorization address asks for an authorization code with PKCE S256, the
/// <see cref="Scope"/>, offline access (a refresh token) and the consent screen every time, so a new sign-in always brings a refresh token.
/// </summary>
public static class GoogleAuthorization
{
    /// <summary>Google's authorization address.</summary>
    public static readonly Uri AuthorizeEndpoint = new("https://accounts.google.com/o/oauth2/v2/auth");

    /// <summary>Google's token address (code exchange and refresh).</summary>
    public static readonly Uri TokenEndpoint = new("https://oauth2.googleapis.com/token");

    /// <summary>Google's revocation address.</summary>
    public static readonly Uri RevokeEndpoint = new("https://oauth2.googleapis.com/revoke");

    /// <summary>The scope asked for: Cloud Text-to-Speech has no narrower one.</summary>
    public const string Scope = "https://www.googleapis.com/auth/cloud-platform";

    /// <summary>The address the browser opens for a sign-in.</summary>
    public static Uri Url(string clientId, Uri redirectUri, string challenge, string state)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        ArgumentNullException.ThrowIfNull(redirectUri);
        ArgumentException.ThrowIfNullOrEmpty(challenge);
        ArgumentException.ThrowIfNullOrEmpty(state);
        var query = new StringBuilder();
        foreach (var (name, value) in new (string, string)[]
        {
            ("client_id", clientId),
            ("redirect_uri", redirectUri.OriginalString),
            ("response_type", "code"),
            ("scope", Scope),
            ("code_challenge", challenge),
            ("code_challenge_method", "S256"),
            ("state", state),
            ("access_type", "offline"),
            ("prompt", "consent"),
        })
        {
            query.Append(query.Length == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }

        return new Uri(AuthorizeEndpoint.AbsoluteUri + query);
    }
}
