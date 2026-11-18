namespace TMConcept.Core.Documents;

/// <summary>
/// How a document file is read (0.1.24): only <see cref="Pages"/> of a PDF (null = every page); <see cref="Recognizer"/> reads the text of a
/// page that has none (a scan; null = such a page is counted and left out); <see cref="Progress"/> gets every page read, under
/// <see cref="Name"/>.
/// </summary>
public sealed record DocumentReadOptions(DocumentPageRange? Pages = null, IPageTextRecognizer? Recognizer = null, IProgress<DocumentProgress>? Progress = null,
    string Name = "")
{
    /// <summary>Every page, no text extraction, no progress.</summary>
    public static DocumentReadOptions All { get; } = new();
}
