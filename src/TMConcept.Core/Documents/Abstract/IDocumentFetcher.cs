namespace TMConcept.Core.Documents;

/// <summary>
/// Fetches a document from the network once, when the user adds it (a web page's readable text, a Wikipedia article): with size and time
/// limits, never running a script. The app keeps what it fetched as its own copy.
/// </summary>
public interface IDocumentFetcher
{
    /// <summary>The kind it fetches.</summary>
    DocumentKind Kind { get; }

    /// <summary>
    /// Fetches <paramref name="address"/> (a web address; for Wikipedia an article's address or its title, then in <paramref name="language"/>).
    /// <see cref="DocumentRejectedException"/> when it cannot be used (not found, too large, not text).
    /// </summary>
    Task<FetchedDocument> FetchAsync(string address, string language, CancellationToken cancellationToken);
}
