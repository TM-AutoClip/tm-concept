namespace TMConcept.Core.Documents;

/// <summary>
/// How a document file is read (0.1.24): only <see cref="Pages"/> of a PDF (null = every page); <see cref="Recognizer"/> reads the text of a
/// page that has none (a scan; null = such a page is counted and left out); <see cref="Progress"/> gets every page read, under
/// <see cref="Name"/>. <see cref="Memory"/> (0.1.25) holds the file's pages read before: such a page is taken as kept, a page read now is kept.
/// </summary>
public sealed record DocumentReadOptions(DocumentPageRange? Pages = null, IPageTextRecognizer? Recognizer = null, IProgress<DocumentProgress>? Progress = null,
    string Name = "")
{
    /// <summary>The kept pages of the file (null = none are kept or reused).</summary>
    public IPageTextMemory? Memory { get; init; }

    /// <summary>Every page, no text extraction, no progress.</summary>
    public static DocumentReadOptions All { get; } = new();
}
