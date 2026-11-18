namespace TMConcept.Core.Hardware;

/// <summary>
/// The user's "Text recognition" setting for the local OCR of scanned pages (0.1.27, per computer, in preferences.json). Stored as a number:
/// a new value is added at the end.
/// </summary>
public enum OcrAcceleration
{
    /// <summary>The graphics card when it is free (Windows, DirectML), otherwise the processor (the default).</summary>
    Automatic = 0,

    /// <summary>Always the processor.</summary>
    Processor = 1,
}
