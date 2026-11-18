namespace TMConcept.Core.Documents;

/// <summary>
/// A document that was added, and why its meaning-search vectors could not be made when that failed (empty = made, or the meaning model is
/// not installed; the document is usable with the keyword search either way).
/// </summary>
public sealed record DocumentAddResult(SourceDocument Document, string EmbeddingError)
{
    /// <summary>The profile had this content already and <see cref="Document"/> is that one (only when the caller asked to reuse it).</summary>
    public bool Existed { get; init; }

    /// <summary>Scanned pages whose text a text-extraction model read (0.1.24).</summary>
    public int PagesRecognized { get; init; }

    /// <summary>Scanned pages whose text extraction failed or had no image to send (0.1.24; the reasons are in the log).</summary>
    public int PagesUnread { get; init; }
}
