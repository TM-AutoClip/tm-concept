using TMConcept.Core.Chat;

namespace TMConcept.Providers.Anthropic;

/// <summary>A parsed Messages API answer.</summary>
public sealed record AnthropicAnswer(string Text, string StopReason, ChatUsage Usage, (string Type, string Raw)? Error, bool Streamed, int SkippedEvents = 0);
