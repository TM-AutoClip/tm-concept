namespace TMConcept.Providers.GoogleAuth;

/// <summary>What a Google Cloud request needs from the sign-in: a valid access token and the project billed (<c>x-goog-user-project</c>).</summary>
public sealed record GoogleCloudAccess(string AccessToken, string ProjectId);
