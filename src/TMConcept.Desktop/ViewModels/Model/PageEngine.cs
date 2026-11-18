using TMConcept.Core.Documents;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The engine that reads scanned pages during one add or read (0.1.25): the recognizer, the vision model's recognizer whose run is charged
/// (null for the local OCR, which is free) and the engine's name for the notes.
/// </summary>
public sealed record PageEngine(IPageTextRecognizer Recognizer, VisionPageRecognizer? Vision, string Name);
