namespace TMConcept.Core.Documents;

/// <summary>Reads the text of one kind of document file (PDF, Word, plain text, Markdown). Managed code only; nothing is run from the file.</summary>
public interface IDocumentExtractor
{
    /// <summary>The kind it reads.</summary>
    DocumentKind Kind { get; }

    /// <summary>The file extensions it reads (lower case, with the dot).</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Largest file of this kind that can be added (a PDF is read page by page, so it may be much larger than a text file).</summary>
    long MaxBytes { get; }

    /// <summary>Reads <paramref name="file"/>; <see cref="InvalidDataException"/> when it is not a readable file of this kind.</summary>
    Task<ExtractedDocument> ExtractAsync(string file, CancellationToken cancellationToken);

    /// <summary>
    /// Reads <paramref name="file"/> as <paramref name="options"/> says (0.1.24: a page range, text extraction of scanned pages, progress);
    /// <see cref="InvalidDataException"/> when it is not a readable file of this kind or the range is not in it.
    /// </summary>
    Task<ExtractedDocument> ExtractAsync(string file, DocumentReadOptions options, CancellationToken cancellationToken);

    /// <summary>A quick look before an add: the pages and how many sampled pages have no text (<see cref="DocumentProbe.None"/> for a file without pages).</summary>
    Task<DocumentProbe> ProbeAsync(string file, CancellationToken cancellationToken);
}
