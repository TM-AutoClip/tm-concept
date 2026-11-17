namespace TMConcept.Core.Research;

/// <summary>
/// What a search found: the plan, the candidates (one entity in another language is its own candidate; the picked ones first), the backends
/// that failed, and whether the time limit stopped it before every backend answered.
/// </summary>
public sealed record ResearchCandidates(ResearchPlan Plan, IReadOnlyList<ResearchCandidate> Candidates, IReadOnlyList<ResearchFailure> Failures, bool Cut);
