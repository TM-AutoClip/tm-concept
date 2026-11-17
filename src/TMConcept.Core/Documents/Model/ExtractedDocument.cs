namespace TMConcept.Core.Documents;

/// <summary>The text read from a document: its title (empty = the file name), its pieces in order, its pages and the pages without text.</summary>
public sealed record ExtractedDocument(string Title, IReadOnlyList<ExtractedSection> Sections, int Pages = 0, int PagesWithoutText = 0)
{
    /// <summary>Characters of text.</summary>
    public int Characters => Sections.Sum(s => s.Text.Length);
}
