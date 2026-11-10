namespace TMConcept.Core.Concepts;

/// <summary>
/// The topics "Suggest topics" brought (short requests for the extra request) and what the call cost. <see cref="Cut"/>: the answer was cut
/// off and these are the complete topics before the cut. <see cref="Repeats"/>: topics of the answer that equaled an earlier suggestion or a
/// concept title and were left out.
/// </summary>
public sealed record TopicSuggestions(IReadOnlyList<string> Topics, decimal CostUsd, bool CostExact, bool Cut = false, int Repeats = 0);
