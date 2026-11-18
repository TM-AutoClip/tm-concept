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

    /// <summary>Pages taken as kept from an earlier read of the same file instead of being read again (0.1.25).</summary>
    public int PagesReused { get; init; }

    /// <summary>Scanned pages whose images are in a format that cannot be read (JPEG 2000, JBIG2, CCITT fax; 0.1.25).</summary>
    public int PagesUnsupported { get; init; }

    /// <summary>Why the text extraction of scanned pages stopped before the end: the provider's error code or <c>error</c> (empty = it did not; the pages read before are kept).</summary>
    public string RecognitionStopped { get; init; } = string.Empty;
}
