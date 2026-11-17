namespace TMConcept.Core.Documents;

/// <summary>
/// A document of a profile's "Belgelerim" (table <c>documents</c>). The app keeps its own copy of the file unencrypted in the data folder
/// (<see cref="StoredFile"/>, relative to the documents folder); the text cut into passages and their vectors are in the encrypted
/// database. Deleting the profile deletes its documents.
/// </summary>
public sealed class SourceDocument
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The profile (channel) it was added in.</summary>
    public Guid ProfileId { get; set; }

    /// <summary>What it was made from.</summary>
    public DocumentKind Kind { get; set; }

    /// <summary>Title (the file name, the page's or the article's title).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The added file's own name (empty for a web page or an article).</summary>
    public string OriginalName { get; set; } = string.Empty;

    /// <summary>The app's copy, relative to the documents folder ("&lt;profile&gt;/&lt;sha256&gt;.pdf").</summary>
    public string StoredFile { get; set; } = string.Empty;

    /// <summary>The address of a web page or article (empty for a file).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>The language code of a Wikipedia article ("tr", "en", …; empty otherwise).</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>SHA-256 of the copy (the same content is not added twice to a profile).</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Bytes of the copy.</summary>
    public long Bytes { get; set; }

    /// <summary>Pages (PDF), or 0.</summary>
    public int Pages { get; set; }

    /// <summary>Pages without a text layer (PDF), or 0.</summary>
    public int PagesWithoutText { get; set; }

    /// <summary>Characters of text kept.</summary>
    public int Characters { get; set; }

    /// <summary>Passages.</summary>
    public int Chunks { get; set; }

    /// <summary>Whether it can be used.</summary>
    public DocumentStatus Status { get; set; }

    /// <summary>A short code for the user's note ("cut": the text was cut at the limit; "no_text": nothing could be read).</summary>
    public string StatusDetail { get; set; } = string.Empty;

    /// <summary>The cutting rules' version it was cut with (<see cref="DocumentLimits.ChunkerVersion"/>).</summary>
    public int ChunkerVersion { get; set; }

    /// <summary>Added (UTC).</summary>
    public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
