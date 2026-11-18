namespace TMConcept.Core.Documents;

/// <summary>
/// The text read from a document: its title (empty = the file name), its pieces in order, its pages (of a page range: the pages read) and
/// the pages left without text.
/// </summary>
public sealed record ExtractedDocument(string Title, IReadOnlyList<ExtractedSection> Sections, int Pages = 0, int PagesWithoutText = 0)
{
    /// <summary>Pages without a text layer whose text a text-extraction model read (0.1.24).</summary>
    public int PagesRecognized { get; init; }

    /// <summary>Pages without a text layer whose text extraction failed (logged; 0.1.24).</summary>
    public int PagesUnread { get; init; }

    /// <summary>Whether reading stopped at <see cref="DocumentLimits.CharactersMax"/> before the last page (0.1.24: the rest is not even read).</summary>
    public bool StoppedAtLimit { get; init; }

    /// <summary>Characters of text.</summary>
    public int Characters => Sections.Sum(s => s.Text.Length);
}
