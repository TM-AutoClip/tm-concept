namespace TMConcept.Core.Concepts;

/// <summary>A concept that failed <see cref="ConceptSceneCheck"/> and goes into the one repair request, with its findings.</summary>
public sealed record ConceptRepairItem(ParsedConcept Concept, IReadOnlyList<SceneProblem> Problems);
