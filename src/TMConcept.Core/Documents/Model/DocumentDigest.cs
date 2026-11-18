namespace TMConcept.Core.Documents;

/// <summary>
/// What a "read everything" run made before the concepts (0.1.24): notes on the chosen pages, each a passage of its document (not saved;
/// the concepts are grounded in them like in passages), the calls it took and what they cost.
/// </summary>
public sealed record DocumentDigest(IReadOnlyList<DocumentChunk> Notes, int Calls, decimal CostUsd);
