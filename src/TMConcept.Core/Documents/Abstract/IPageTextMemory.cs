namespace TMConcept.Core.Documents;

/// <summary>
/// The kept page texts of one PDF file while it is read (0.1.25, <see cref="PageTextMemory"/>): a kept page is taken as it is, a page read
/// now is kept (an engine's page at once, so a cancel or a stop loses none of what was paid or waited for).
/// </summary>
public interface IPageTextMemory
{
    /// <summary>The kept page <paramref name="page"/>; null when it was never read.</summary>
    DocumentPageText? Find(int page);

    /// <summary>Keeps what <paramref name="source"/> read on <paramref name="page"/>.</summary>
    Task KeepAsync(int page, string text, string source, CancellationToken cancellationToken);

    /// <summary>Saves what is not saved yet.</summary>
    Task FlushAsync(CancellationToken cancellationToken);
}
