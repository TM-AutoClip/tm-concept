namespace TMConcept.Documents.Ocr;

/// <summary>
/// The local OCR on the graphics card (0.1.27, Windows): the DirectML runtime of <paramref name="store"/> (loaded the first time), the
/// recogniser's width padded to multiples of <see cref="LocalOcrModels.RecognizerWidthMin"/>, the detector's input as it is (already a
/// multiple of <see cref="LocalOcrModels.DetectorStride"/>).
/// </summary>
public sealed class DirectMlOcrNetworkFactory(DirectMlRuntimeStore store) : IOcrNetworkFactory
{
    /// <inheritdoc />
    public string Device => "graphics card (DirectML)";

    /// <inheritdoc />
    public OcrNetworkPair Open(string folder)
    {
        if (!OperatingSystem.IsWindows() || !store.Ready)
        {
            throw new InvalidOperationException("The DirectML runtime is not installed on this computer.");
        }

        var runtime = DirectMlRuntime.Load(store.Folder);
        var detector = new DirectMlOcrNetwork(runtime, Path.Combine(folder, LocalOcrModels.DetectorFile), 0);
        try
        {
            return new OcrNetworkPair(detector, new DirectMlOcrNetwork(runtime, Path.Combine(folder, LocalOcrModels.RecognizerFile), LocalOcrModels.RecognizerWidthMin));
        }
        catch
        {
            detector.Dispose();
            throw;
        }
    }
}
