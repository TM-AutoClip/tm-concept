namespace TMConcept.Documents.Ocr;

/// <summary>
/// Opens the local OCR's two networks on one device (0.1.27): the processor (<see cref="CpuOcrNetworkFactory"/>) or the graphics card
/// (<see cref="DirectMlOcrNetworkFactory"/>, Windows). The tests give fakes.
/// </summary>
public interface IOcrNetworkFactory
{
    /// <summary>The device's name in the log ("processor", "graphics card (DirectML)").</summary>
    string Device { get; }

    /// <summary>
    /// The detector and the recogniser from the models in <paramref name="folder"/>; <see cref="InvalidOperationException"/> or
    /// <see cref="InvalidDataException"/> when they cannot be opened on this device.
    /// </summary>
    OcrNetworkPair Open(string folder);
}
