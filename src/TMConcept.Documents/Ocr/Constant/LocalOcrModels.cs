namespace TMConcept.Documents.Ocr;

/// <summary>
/// The local OCR engine chosen for scanned pages (0.1.24 decision; NOT shipped yet, see <see cref="OcrRecognizerBase"/>): PaddleOCR's
/// PP-OCR models (Apache-2.0) in their ONNX form as RapidOCR publishes them (Apache-2.0), run in this process by Microsoft.ML.OnnxRuntime (MIT,
/// already bundled for the meaning model), so the single-file app needs no new native library on Windows or Linux. Two small models: a text
/// line detector and the Latin-script recogniser, whose character set has the Turkish letters (ç, ğ, ı, İ, ö, ş, ü) next to the other Latin
/// languages. They would be an optional component downloaded only after a confirmation, each file pinned by its SHA-256 at a fixed commit, like
/// the meaning model; the addresses and hashes are filled in when the files are checked (nothing unverified is listed here). Tesseract was
/// left aside: its Apache-2.0 engine is a native library per platform (no managed build), which a trimmed single-file app cannot carry cleanly.
/// </summary>
public static class LocalOcrModels
{
    /// <summary>The component's code (reserved).</summary>
    public const string ComponentCode = "ocr.pp-ocr-latin";

    /// <summary>The engine's display name.</summary>
    public const string Name = "PaddleOCR (PP-OCR, Latin script)";

    /// <summary>The models' licence (PaddleOCR and RapidOCR alike).</summary>
    public const string License = "apache-2.0";

    /// <summary>The detector's input side is rounded to a multiple of this (the DB detector's stride).</summary>
    public const int DetectorStride = 32;

    /// <summary>The recogniser reads a line scaled to this height (pixels).</summary>
    public const int RecognizerHeight = 48;
}
