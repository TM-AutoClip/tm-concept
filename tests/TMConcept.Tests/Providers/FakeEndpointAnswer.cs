namespace TMConcept.Tests.Providers;

/// <summary>One queued answer of <see cref="FakeEndpointServer"/>.</summary>
internal sealed record FakeEndpointAnswer(int Status, string Body, string MediaType);
