namespace TMConcept.Core.Documents;

/// <summary>
/// The plan of a "read everything" run before it starts (0.1.24): the chosen documents' pages and passages and their tokens, the summary
/// calls (map), the merge calls (reduce), the notes the concepts then read, and the estimate of those calls (<see cref="WorkEstimate"/>).
/// </summary>
public sealed record DocumentDigestPlan(int Documents, int Pages, int Passages, long PassageTokens, int MapCalls, int ReduceCalls, int Notes, WorkEstimate Estimate)
{
    /// <summary>Every model call before the concepts.</summary>
    public int Calls => MapCalls + ReduceCalls;
}
