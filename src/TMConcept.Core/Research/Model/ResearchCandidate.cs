namespace TMConcept.Core.Research;

/// <summary>A page a search found, shown before anything is fetched: the hit and whether the plan picked it (the user can change it).</summary>
public sealed record ResearchCandidate(ResearchHit Hit, bool Picked);
