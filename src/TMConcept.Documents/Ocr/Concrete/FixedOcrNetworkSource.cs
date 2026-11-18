namespace TMConcept.Documents.Ocr;

/// <summary>The same two networks for every page (the processor; the tests' fakes), closed with the source.</summary>
public sealed class FixedOcrNetworkSource(IOcrNetwork detector, IOcrNetwork recognizer) : IOcrNetworkSource
{
    private readonly OcrNetworkPair _pair = new(detector, recognizer);

    /// <inheritdoc />
    public Task<OcrPageNetworks> ForPageAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new OcrPageNetworks(_pair.Detector, _pair.Recognizer, null));

    /// <inheritdoc />
    public void Failed(OcrPageNetworks networks, Exception error)
    {
        // Never on the graphics card: nothing to give up.
    }

    /// <inheritdoc />
    public void Dispose() => _pair.Dispose();
}
