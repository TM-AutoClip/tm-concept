namespace TMConcept.Documents.Ocr;

/// <summary>
/// Gives the local OCR the networks for each page (0.1.27): the graphics card's when it can be had for that page, otherwise the processor's
/// (<see cref="HybridOcrNetworkSource"/>), or always the same two (<see cref="FixedOcrNetworkSource"/>). Disposing it closes what it opened
/// for the processor.
/// </summary>
public interface IOcrNetworkSource : IDisposable
{
    /// <summary>The networks for the next page; the caller disposes <see cref="OcrPageNetworks.Lease"/> after the page.</summary>
    Task<OcrPageNetworks> ForPageAsync(CancellationToken cancellationToken);

    /// <summary>The networks on the graphics card failed on a page: the card is given up for this run (the page is read again on the processor).</summary>
    void Failed(OcrPageNetworks networks, Exception error);
}
