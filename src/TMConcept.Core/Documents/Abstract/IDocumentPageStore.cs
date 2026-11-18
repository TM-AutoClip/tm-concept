namespace TMConcept.Core.Documents;

/// <summary>
/// The kept page texts of the profiles' PDFs (0.1.25, <see cref="DocumentPageText"/>; the encrypted database) and the re-cutting of a document
/// whose missing pages were read later.
/// </summary>
public interface IDocumentPageStore
{
    /// <summary>The kept pages <paramref name="from"/>–<paramref name="to"/> of the profile's file <paramref name="fileSha256"/>.</summary>
    Task<IReadOnlyList<DocumentPageText>> PagesAsync(Guid profileId, string fileSha256, int from, int to, CancellationToken cancellationToken);

    /// <summary>Saves pages (a page of the same profile, file and number is replaced).</summary>
    Task SavePagesAsync(IReadOnlyList<DocumentPageText> pages, CancellationToken cancellationToken);

    /// <summary>Which pages of the profile's files are kept, with their length and engine (no text is read).</summary>
    Task<IReadOnlyList<DocumentPageMark>> MarksAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>Deletes the kept pages of the profile's file <paramref name="fileSha256"/>.</summary>
    Task ForgetAsync(Guid profileId, string fileSha256, CancellationToken cancellationToken);

    /// <summary>Saves <paramref name="document"/>'s new counts with its new passages in place of the old ones and their vectors (all or nothing).</summary>
    Task ReplaceAsync(SourceDocument document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken);
}
