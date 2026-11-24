namespace TMConcept.Providers.GoogleAuth;

/// <summary>The user's own desktop OAuth client ("installed" in the file Google downloads).</summary>
public sealed record GoogleOAuthClient(string ClientId, string ClientSecret);
