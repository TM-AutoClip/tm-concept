namespace TMConcept.Core.Documents;

/// <summary>One passage of a document's text (table <c>document_chunks</c>; deleted with its document).</summary>
public sealed class DocumentChunk
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The document.</summary>
    public Guid DocumentId { get; set; }

    /// <summary>The document's profile (searches read one profile's passages).</summary>
    public Guid ProfileId { get; set; }

    /// <summary>Its place in the document (0, 1, …).</summary>
    public int Ordinal { get; set; }

    /// <summary>The page it starts on (PDF), or 0.</summary>
    public int Page { get; set; }

    /// <summary>The heading it is under ("Chapter 2 › Trade"), or empty.</summary>
    public string Heading { get; set; } = string.Empty;

    /// <summary>The text.</summary>
    public string Text { get; set; } = string.Empty;
}
