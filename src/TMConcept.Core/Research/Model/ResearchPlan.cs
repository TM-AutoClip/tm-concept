namespace TMConcept.Core.Research;

/// <summary>
/// The fixed plan of a run (<see cref="ResearchPlanner"/>): every language read, in order of preference (the profile's first, then English,
/// then the ones the subject or the user adds), the languages searched directly, and the queries (one per backend and searched language).
/// </summary>
public sealed record ResearchPlan(string Subject, IReadOnlyList<string> Languages, IReadOnlyList<string> SearchLanguages, IReadOnlyList<ResearchPlannedQuery> Queries);
