namespace TMConcept.Core.Concepts;

/// <summary>
/// The outcome of an AI edit: the rewritten concept (null when nothing came: <see cref="ErrorCode"/> says why: "truncated", "parse",
/// "canceled" or a provider error code), what the call cost and whether that cost is exact (a canceled call counts the prompt as an estimate).
/// Nothing is saved by the edit itself: the page shows the result in the editor and the user saves it.
/// </summary>
public sealed record ConceptEditResult(ParsedConcept? Concept, decimal CostUsd, bool CostExact, string ErrorCode)
{
    /// <summary>Whether a rewritten concept came.</summary>
    public bool Succeeded => Concept is not null;
}
