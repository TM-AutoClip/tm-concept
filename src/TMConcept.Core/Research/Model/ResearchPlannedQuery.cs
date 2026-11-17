namespace TMConcept.Core.Research;

/// <summary>One query of a plan: which backend gets it and what it asks.</summary>
public sealed record ResearchPlannedQuery(string BackendId, ResearchQuery Query);
