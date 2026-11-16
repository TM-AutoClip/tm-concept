namespace TMConcept.Core.Endpoints;

/// <summary>A model a local server lists at <c>GET {base}/models</c>; <paramref name="ContextTokens"/> is 0 when the server does not say.</summary>
public sealed record ListedModel(string Id, long ContextTokens);
