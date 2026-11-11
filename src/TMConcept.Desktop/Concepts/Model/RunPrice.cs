using TMConcept.Core.Costs;

namespace TMConcept.Desktop.Concepts;

/// <summary>
/// What a run of a brief would cost with the models chosen now: the concept model's name, the whole estimate (the meaning check's
/// embeddings included) and how many concepts the meaning check embeds first (<see cref="Backfill"/>, 0 without an embedding model).
/// </summary>
public sealed record RunPrice(string ModelName, CostEstimate Estimate, int Backfill);
