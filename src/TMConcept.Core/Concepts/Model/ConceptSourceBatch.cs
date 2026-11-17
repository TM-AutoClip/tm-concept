using TMConcept.Core.Documents;

namespace TMConcept.Core.Concepts;

/// <summary>
/// What one batch of a source sends: the extra request line (empty = none), the batch's topics (one concept each; null = no TOPICS block),
/// the document passages (null = not a document run) and a lower answer limit when the passages need the room (null = the usual one).
/// <see cref="Size"/>: fewer concepts than asked when only that many leave room for the source's material (null = as asked; the generator
/// then splits the rest into batches of this size). <see cref="EstimatedTokens"/>: for an estimate, the tokens of the material the prompt
/// does not hold (passages are not read just for a price).
/// </summary>
public sealed record ConceptSourceBatch(string ExtraPrompt, IReadOnlyList<string>? Topics, DocumentGrounding? Grounding = null, int? MaxOutputTokens = null,
    int? Size = null, long EstimatedTokens = 0);
