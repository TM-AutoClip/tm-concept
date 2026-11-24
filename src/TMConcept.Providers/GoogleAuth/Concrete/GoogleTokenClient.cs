using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using TMConcept.Core.Providers;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.GoogleAuth;

/// <summary>
/// Google's token address (form posts): the code of a sign-in for tokens (with the PKCE verifier and the same redirect address), a refresh
/// token for a new access token, and the revocation of a token at sign-out. Through the shared HTTP layer, so a failure is a
/// <see cref="ProviderException"/> whose text never holds the token sent.
/// </summary>
public sealed class GoogleTokenClient(ProviderHttp http, TimeProvider clock)
{
    private const string Name = "Google";

    /// <summary>Turns the code of a sign-in into tokens.</summary>
    public async Task<GoogleTokens> ExchangeAsync(GoogleOAuthClient client, string code, string verifier, Uri redirectUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(redirectUri);
        return await PostAsync(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = client.ClientId,
            ["client_secret"] = client.ClientSecret,
            ["redirect_uri"] = redirectUri.OriginalString,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier,
        }, code, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A new access token for a refresh token (the refresh token of the answer is empty when Google keeps the old one).</summary>
    public async Task<GoogleTokens> RefreshAsync(GoogleOAuthClient client, string refreshToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        return await PostAsync(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = client.ClientId,
            ["client_secret"] = client.ClientSecret,
            ["grant_type"] = "refresh_token",
        }, refreshToken, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Revokes a token at Google (a refresh token revokes the whole sign-in).</summary>
    public async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        await http.SendAsync(() => new HttpRequestMessage(HttpMethod.Post, GoogleAuthorization.RevokeEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }),
        }, Name, token, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GoogleTokens> PostAsync(Dictionary<string, string> form, string secret, CancellationToken cancellationToken)
    {
        var reply = await http.SendAsync(() => new HttpRequestMessage(HttpMethod.Post, GoogleAuthorization.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        }, Name, secret, cancellationToken).ConfigureAwait(false);
        return Parse(reply.Body, clock.GetUtcNow());
    }

    /// <summary>Reads an answer of the token address; the access token ends <c>expires_in</c> seconds after <paramref name="now"/>.</summary>
    public static GoogleTokens Parse(byte[] body, DateTimeOffset now)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new ProviderException("Google returned a token answer that could not be read.", ex) { Code = "http" };
        }

        var access = Text(root?["access_token"]);
        if (access.Length == 0)
        {
            throw new ProviderException("Google returned no access token.") { Code = "http" };
        }

        var seconds = root?["expires_in"] is JsonValue value && (value.TryGetValue<long>(out var number)
            || (value.TryGetValue<string>(out var text) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)))
            ? number
            : 3600;
        return new GoogleTokens(access, now.AddSeconds(Math.Clamp(seconds, 60, 86_400)), Text(root?["refresh_token"]));
    }

    private static string Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text.Trim() : string.Empty;
}
