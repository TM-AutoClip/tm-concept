namespace TMConcept.Core.Concepts;

/// <summary>The topics "Suggest topics" brought (short requests for the extra request) and what the call cost.</summary>
public sealed record TopicSuggestions(IReadOnlyList<string> Topics, decimal CostUsd, bool CostExact);
