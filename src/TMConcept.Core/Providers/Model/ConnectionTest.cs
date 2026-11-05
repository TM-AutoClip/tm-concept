namespace TMConcept.Core.Providers;

/// <summary>Result of a key test: success, or the failure code (<c>auth</c>, <c>network</c> …) with a short detail (no secrets).</summary>
public sealed record ConnectionTest(bool Ok, string Code, string Detail);
