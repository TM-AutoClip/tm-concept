namespace TMConcept.Documents.Ocr;

/// <summary>The local OCR's two networks opened on one device (0.1.27); disposing the pair closes both.</summary>
public sealed class OcrNetworkPair(IOcrNetwork detector, IOcrNetwork recognizer) : IDisposable
{
    private int _disposed;

    /// <summary>The text line detector.</summary>
    public IOcrNetwork Detector { get; } = detector;

    /// <summary>The line recogniser.</summary>
    public IOcrNetwork Recognizer { get; } = recognizer;

    /// <summary>Whether it was disposed.</summary>
    public bool Disposed => Volatile.Read(ref _disposed) != 0;

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            Detector.Dispose();
        }
        finally
        {
            Recognizer.Dispose();
        }
    }
}
