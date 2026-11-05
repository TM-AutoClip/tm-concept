namespace TMConcept.Tests.Providers;

/// <summary>A request the fake network saw.</summary>
internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string Body);
