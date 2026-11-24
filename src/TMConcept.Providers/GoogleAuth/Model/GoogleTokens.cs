namespace TMConcept.Providers.GoogleAuth;

/// <summary>An answer of Google's token address: the access token, when it ends (UTC) and the refresh token (empty when none came).</summary>
public sealed record GoogleTokens(string AccessToken, DateTimeOffset ExpiresAtUtc, string RefreshToken);
