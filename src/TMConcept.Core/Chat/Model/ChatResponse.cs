namespace TMConcept.Core.Chat;

/// <summary>An answer: text, usage, the cost when the provider reports it, the stop reason and the request id.</summary>
public sealed record ChatResponse(string Text, ChatUsage Usage, decimal? ReportedCostUsd, string StopReason, string RequestId);
