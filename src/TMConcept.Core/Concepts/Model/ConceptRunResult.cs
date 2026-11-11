using TMConcept.Core.Runs;

namespace TMConcept.Core.Concepts;

/// <summary>
/// The result of a whole "Generate" press. <see cref="SemanticSkipped"/>: batches whose optional semantic duplicate check could not run
/// (the reason is in the log; the page tells the user). <see cref="TopicsLeft"/>: the selected topics (of the ones this run used, at most
/// <see cref="ConceptLimits.CountMax"/>) that got no concept: those of batches that never ran or failed, and in a cut or short batch the
/// ones after the concepts that came back (concepts come back in topic order).
/// </summary>
public sealed record ConceptRunResult(Run Run, IReadOnlyList<Concept> Accepted, int RejectedDuplicates, int Problems, string ErrorCode, string ErrorDetail,
    int SemanticSkipped = 0, IReadOnlyList<string>? TopicsLeft = null)
{
    /// <summary>How many are missing from the requested count (duplicates thrown away or a stopped run). "Fill the missing" uses it.</summary>
    public int Missing => Math.Max(0, Run.Requested - Accepted.Count);

    /// <summary>The selected topics that got no concept (empty when the run had no topics or wrote one for each).</summary>
    public IReadOnlyList<string> TopicsLeft { get; init; } = TopicsLeft ?? [];
}
