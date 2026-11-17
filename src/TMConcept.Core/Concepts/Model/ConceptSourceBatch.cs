using TMConcept.Core.Documents;

namespace TMConcept.Core.Concepts;

/// <summary>
/// What one batch of a source sends: the extra request line (empty = none), the batch's topics (one concept each; null = no TOPICS block),
/// the document passages (null = not a document run) and a lower answer limit when the passages need the room (null = the usual one).
/// </summary>
public sealed record ConceptSourceBatch(string ExtraPrompt, IReadOnlyList<string>? Topics, DocumentGrounding? Grounding = null, int? MaxOutputTokens = null);
