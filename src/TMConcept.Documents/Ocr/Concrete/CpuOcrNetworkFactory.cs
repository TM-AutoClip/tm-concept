namespace TMConcept.Documents.Ocr;

/// <summary>The local OCR on the processor (0.1.25 behaviour; the bundled ONNX Runtime): <see cref="OnnxOcrNetwork"/>.</summary>
public sealed class CpuOcrNetworkFactory : IOcrNetworkFactory
{
    /// <inheritdoc />
    public string Device => "processor";

    /// <inheritdoc />
    public OcrNetworkPair Open(string folder)
    {
        var detector = new OnnxOcrNetwork(Path.Combine(folder, LocalOcrModels.DetectorFile));
        try
        {
            return new OcrNetworkPair(detector, new OnnxOcrNetwork(Path.Combine(folder, LocalOcrModels.RecognizerFile)));
        }
        catch
        {
            detector.Dispose();
            throw;
        }
    }
}
