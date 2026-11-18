namespace TMConcept.Core.Documents;

/// <summary>
/// The text of one page of a PDF as read once and kept for later (table <c>document_pages</c>, 0.1.25): its text layer, or what the local OCR or
/// a vision model read on a scanned page (empty = the engine found no text there), with which engine and when. Kept per profile and per FILE
/// (<see cref="FileSha256"/>, the SHA-256 of the whole PDF), so another page range of the same file, a later add after a cancel and a read of
/// the missing pages reuse it instead of reading again; deleted with the last document of that file in the profile, or with the profile.
/// </summary>
public sealed class DocumentPageText
{
    /// <summary>The profile.</summary>
    public Guid ProfileId { get; set; }

    /// <summary>SHA-256 of the whole PDF file.</summary>
    public string FileSha256 { get; set; } = string.Empty;

    /// <summary>The page number in the file (1, 2, …).</summary>
    public int Page { get; set; }

    /// <summary>The page's text as read (not cleaned yet; empty = no text on it).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>What produced it (<see cref="PageTextSources"/>: the text layer, <c>ocr:…</c> or <c>vision:provider/model</c>).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>When it was read (UTC).</summary>
    public DateTimeOffset ReadAtUtc { get; set; }
}
