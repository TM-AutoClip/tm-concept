using TMConcept.Core.Runs;

namespace TMConcept.Core.Concepts;

/// <summary>
/// The result of a whole concept run. <see cref="SemanticSkipped"/>: batches whose optional semantic duplicate check could not run (the
/// reason is in the log; the page tells the user). Which topics got a concept is told batch by batch (<see cref="ConceptBatch.TopicsMade"/>).
/// </summary>
public sealed record ConceptRunResult(Run Run, IReadOnlyList<Concept> Accepted, int RejectedDuplicates, int Problems, string ErrorCode, string ErrorDetail,
    int SemanticSkipped = 0)
{
    /// <summary>
    /// How many are missing from the requested count (duplicates thrown away or a stopped run). Idea runs offer "Fill the missing" with it;
    /// a topic run does not need it: its unreached topics simply stay ticked.
    /// </summary>
    public int Missing => Math.Max(0, Run.Requested - Accepted.Count);
}
