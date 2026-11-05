using System.Text.Json.Nodes;

namespace TMConcept.Core.Concepts;

/// <summary>The concept prompt: the fixed part (rules, channel, concept; cached) and the changing part (history, extra request, count).</summary>
public sealed record ConceptPrompt(string System, string User, JsonObject Schema);
