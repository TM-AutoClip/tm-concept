using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;

namespace TMConcept.Documents.Web;

/// <summary>
/// The common part of fetching a document: only http and https, and only from the public internet (<see cref="NetworkAddresses"/>: an
/// address on this computer or the local network is refused, also when a redirect leads there; the real transport checks the resolved
/// addresses too); the app's own HTTP layer (<see cref="DocumentTransport"/>) with one time limit for the whole fetch, the body included
/// (<see cref="DocumentLimits.WebSecondsMax"/>: a server that sends its page a few bytes a minute is cut off), a size limit
/// (<see cref="DocumentLimits.WebBytesMax"/>, also when the server does not say the size) and at most <see cref="RedirectsMax"/> redirects; a
/// generic User-Agent that names the app and its version, nothing about the user; 404 is "not found", any other failure says the system's
/// reason (<see cref="DocumentRejectedException"/>).
/// </summary>
public abstract class DocumentFetcherBase(DocumentTransport transport, ILogger logger) : IDocumentFetcher
{
    private readonly SafeHttp _http = new(transport, logger);

    /// <summary>The User-Agent of every document request.</summary>
    public static string UserAgent => SafeHttp.UserAgent;

    /// <summary>The fetcher's log.</summary>
    protected ILogger Logger { get; } = logger;

    /// <summary>Redirects followed at most.</summary>
    public const int RedirectsMax = SafeHttp.RedirectsMax;

    /// <inheritdoc />
    public abstract DocumentKind Kind { get; }

    /// <inheritdoc />
    public abstract Task<FetchedDocument> FetchAsync(string address, string language, CancellationToken cancellationToken);

    /// <summary>An absolute http(s) address of <paramref name="address"/> ("example.org/page" gets https); null when it is not one.</summary>
    public static Uri? WebAddress(string address)
    {
        var text = (address ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && uri.Host.Length > 0
            ? uri
            : null;
    }

    /// <summary>Refuses an address whose host is this computer or the local network (<see cref="NetworkAddresses.IsLocalHost"/>).</summary>
    public static void RefuseLocal(Uri address) => SafeHttp.RefuseLocal(address);

    /// <summary>Fetches <paramref name="address"/> within the limits (<see cref="SafeHttp"/>).</summary>
    protected Task<WebResponse> GetAsync(Uri address, CancellationToken cancellationToken) => _http.GetAsync(address, cancellationToken);
}
