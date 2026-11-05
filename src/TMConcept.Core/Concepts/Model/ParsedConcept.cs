namespace TMConcept.Core.Concepts;

/// <summary>A concept read from a model answer (cleaned, not saved yet).</summary>
public sealed record ParsedConcept(string Title, string Summary, string Content);
