using TMConcept.Core.Tools;

namespace TMConcept.Documents.Ocr;

/// <summary>
/// The local OCR engine for scanned pages (0.1.25): PaddleOCR's PP-OCRv5 models (Apache-2.0) in their ONNX form as RapidOCR publishes them
/// (Apache-2.0; ModelScope RapidAI/RapidOCR, tag v3.10.0, checked on 18 Nov 2026), run in this process by Microsoft.ML.OnnxRuntime (MIT,
/// already bundled for the meaning model), so the single-file app needs no new native library on Windows or Linux. Two small models: the
/// PP-OCRv5 mobile text line detector (DB, multilingual) and the PP-OCRv5 Latin-script recogniser (CTC), whose character list
/// (<see cref="DictionaryResource"/>, bundled) has the Turkish letters ç, ğ, ı, İ, ö, ş, ü and their capitals next to the other Latin
/// languages. An optional component, downloaded only after a confirmation, each file pinned by its SHA-256. Tesseract was left aside: its
/// engine is a native library per platform, which a trimmed single-file app cannot carry cleanly.
/// </summary>
public static class LocalOcrModels
{
    /// <summary>The component's code.</summary>
    public const string ComponentCode = Core.Documents.PageTextEngines.LocalOcrComponent;

    /// <summary>The engine's display name.</summary>
    public const string Name = "PaddleOCR PP-OCRv5 (Latin script)";

    /// <summary>The models' licence (PaddleOCR and RapidOCR alike).</summary>
    public const string License = "apache-2.0";

    /// <summary>The detector's input side is rounded to a multiple of this (the DB detector's stride).</summary>
    public const int DetectorStride = 32;

    /// <summary>The longest side of the image the detector looks at (a 300 dpi A4 scan is made about 0.45 times smaller; memory stays small).</summary>
    public const int DetectorSideMax = 1_600;

    /// <summary>The recogniser reads a line scaled to this height (pixels).</summary>
    public const int RecognizerHeight = 48;

    /// <summary>The narrowest input of the recogniser (a short word is padded to it, as PaddleOCR does).</summary>
    public const int RecognizerWidthMin = 320;

    /// <summary>The widest input of the recogniser (a longer line is squeezed to it).</summary>
    public const int RecognizerWidthMax = 3_200;

    /// <summary>The recogniser's character list, bundled (PaddleOCR <c>ppocrv5_latin_dict.txt</c>, Apache-2.0; one character a line).</summary>
    public const string DictionaryResource = "TMConcept.Documents.Ocr.ppocrv5_latin_dict.txt";

    /// <summary>The detector's file name on disk.</summary>
    public const string DetectorFile = "ch_PP-OCRv5_det_mobile.onnx";

    /// <summary>The recogniser's file name on disk.</summary>
    public const string RecognizerFile = "latin_PP-OCRv5_rec_mobile.onnx";

    private const string Repository = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/onnx/PP-OCRv5/";

    /// <summary>The text line detector (PP-OCRv5 mobile, 4.8 MB).</summary>
    public static readonly ToolArchive Detector = new(ToolKind.OcrWeights, "3.10.0", "any", new Uri(Repository + "det/" + DetectorFile),
        "4d97c44a20d30a81aad087d6a396b08f786c4635742afc391f6621f5c6ae78ae", string.Empty, DetectorFile, 4_819_576); // SHA-256 of the public file

    /// <summary>The Latin-script recogniser (PP-OCRv5 mobile, 7.9 MB).</summary>
    public static readonly ToolArchive Recognizer = new(ToolKind.OcrWeights, "3.10.0", "any", new Uri(Repository + "rec/" + RecognizerFile),
        "b20bd37c168a570f583afbc8cd7925603890efbcdc000a59e22c269d160b5f5a", string.Empty, RecognizerFile, 7_904_513); // SHA-256 of the public file

    /// <summary>Both files.</summary>
    public static readonly IReadOnlyList<ToolArchive> Files = [Detector, Recognizer];
}
