namespace TMConcept.Core.Endpoints;

/// <summary>
/// Result of asking a local server for its models: <paramref name="Ok"/> with the models, or the failure <paramref name="Code"/>
/// (<c>address</c> for an address that is not http/https, <c>unreadable</c> for an answer that is not a model list, or a provider code:
/// <c>network</c>, <c>timeout</c>, <c>auth</c> …) with a short detail that never holds the token.
/// </summary>
public sealed record EndpointListing(bool Ok, string Code, string Detail, IReadOnlyList<ListedModel> Models);
