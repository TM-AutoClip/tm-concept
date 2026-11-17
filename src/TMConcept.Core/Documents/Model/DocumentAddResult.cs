namespace TMConcept.Core.Documents;

/// <summary>
/// A document that was added, and why its meaning-search vectors could not be made when that failed (empty = made, or the meaning model is
/// not installed; the document is usable with the keyword search either way).
/// </summary>
public sealed record DocumentAddResult(SourceDocument Document, string EmbeddingError);
