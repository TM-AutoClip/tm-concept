namespace TMConcept.Core.Concepts;

/// <summary>
/// The topics "Suggest topics" brought (already saved as <see cref="TopicStatus.Offered"/>, with their ids, so the page can select them) and
/// what the call cost. <see cref="Cut"/>: the answer was cut off and these are the complete topics before the cut. <see cref="Repeats"/>:
/// topics of the answer that equaled an earlier suggestion or a concept title and were left out.
/// </summary>
public sealed record TopicSuggestions(IReadOnlyList<TopicSuggestion> Topics, decimal CostUsd, bool CostExact, bool Cut = false, int Repeats = 0);
