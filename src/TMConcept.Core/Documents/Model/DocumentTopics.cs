namespace TMConcept.Core.Documents;

/// <summary>What "Find topics" brought: the topics, what the call cost and whether the answer was cut off.</summary>
public sealed record DocumentTopics(IReadOnlyList<DocumentTopic> Topics, decimal CostUsd, bool Cut);
