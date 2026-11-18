namespace TMConcept.Core.Documents;

/// <summary>What produced a kept page text (<see cref="DocumentPageText.Source"/>, 0.1.25).</summary>
public static class PageTextSources
{
    /// <summary>The page's own text layer (read by the PDF reader).</summary>
    public const string Layer = "layer";

    /// <summary>Prefix of the local OCR ("ocr:pp-ocr-latin").</summary>
    public const string OcrPrefix = "ocr:";

    /// <summary>Prefix of a vision model ("vision:anthropic/claude-haiku-5-5", "vision:app-ollama/gemma3:4b").</summary>
    public const string VisionPrefix = "vision:";

    /// <summary>An engine that does not name itself (a test's).</summary>
    public const string Unknown = "engine";
}
