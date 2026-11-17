namespace TMConcept.Core.Documents;

/// <summary>Reads the text of one kind of document file (PDF, Word, plain text, Markdown). Managed code only; nothing is run from the file.</summary>
public interface IDocumentExtractor
{
    /// <summary>The kind it reads.</summary>
    DocumentKind Kind { get; }

    /// <summary>The file extensions it reads (lower case, with the dot).</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Reads <paramref name="file"/>; <see cref="InvalidDataException"/> when it is not a readable file of this kind.</summary>
    Task<ExtractedDocument> ExtractAsync(string file, CancellationToken cancellationToken);
}
