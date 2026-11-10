using System.Text.Json.Nodes;

namespace TMConcept.Core.Concepts;

/// <summary>A built prompt: the fixed part (rules, channel content; cached), the changing part (the profile's past, the request) and the answer schema.</summary>
public sealed record ConceptPrompt(string System, string User, JsonObject Schema);
