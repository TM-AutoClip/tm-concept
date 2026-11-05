namespace TMConcept.Core.Concepts;

/// <summary>Concepts read from one model answer, problems of single items and whether the answer was cut off.</summary>
public sealed record ConceptParseResult(IReadOnlyList<ParsedConcept> Concepts, IReadOnlyList<string> Problems, bool Truncated);
