using TMConcept.Core.Runs;

namespace TMConcept.Core.Concepts;

/// <summary>
/// The result of a whole "Generate" press. <see cref="SemanticSkipped"/>: batches whose optional semantic duplicate check could not run
/// (the reason is in the log; the page tells the user).
/// </summary>
public sealed record ConceptRunResult(Run Run, IReadOnlyList<Concept> Accepted, int RejectedDuplicates, int Problems, string ErrorCode, string ErrorDetail,
    int SemanticSkipped = 0)
{
    /// <summary>How many are missing from the requested count (duplicates thrown away or a stopped run). "Fill the missing" uses it.</summary>
    public int Missing => Math.Max(0, Run.Requested - Accepted.Count);
}
