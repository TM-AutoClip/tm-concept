using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
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
    /// <summary>The User-Agent of every document request.</summary>
    public static string UserAgent { get; } = "TM-Concept/" + (typeof(DocumentFetcherBase).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0")
        + " (open-source desktop app)";

    /// <summary>The fetcher's log.</summary>
    protected ILogger Logger { get; } = logger;

    /// <summary>Redirects followed at most.</summary>
    public const int RedirectsMax = 5;

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
    public static void RefuseLocal(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (NetworkAddresses.IsLocalHost(address.Host))
        {
            throw new DocumentRejectedException("private", "The address is on this computer or the local network.", address.Host);
        }
    }

    /// <summary>Fetches <paramref name="address"/> within the limits.</summary>
    protected async Task<WebResponse> GetAsync(Uri address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(transport.TimeLimit);
        using var client = new HttpClient(transport.CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        var current = address;
        try
        {
            for (var hop = 0; ; hop++)
            {
                RefuseLocal(current);
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.UserAgent.ParseAdd(UserAgent);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.5));
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
                if (Redirect(response, current) is { } next)
                {
                    if (hop >= RedirectsMax)
                    {
                        throw new DocumentRejectedException("network", "The address redirected too many times.", address.ToString());
                    }

                    current = next;
                    continue;
                }

                return await ReadAsync(response, current, limit.Token).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException ex) when (ex.InnerException is DocumentRejectedException rejected)
        {
            Logger.LogWarning("Document fetch refused: {Host} is on this computer or the local network", current.Host);
            throw rejected;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Document fetch from {Host} failed", current.Host);
            throw new DocumentRejectedException("network", "The address could not be reached.", ex.Message, ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "Document fetch from {Host} timed out", current.Host);
            throw new DocumentRejectedException("network", "The address did not answer in time.",
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{transport.TimeLimit.TotalSeconds:0.#} s"), ex);
        }
    }

    /// <summary>The address a redirect answer leads to (relative ones resolved); null when the answer is no redirect.</summary>
    private static Uri? Redirect(HttpResponseMessage response, Uri current)
    {
        if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308) || response.Headers.Location is not { } location)
        {
            return null;
        }

        var next = location.IsAbsoluteUri ? location : new Uri(current, location);
        return next.Scheme == Uri.UriSchemeHttps || next.Scheme == Uri.UriSchemeHttp
            ? next
            : throw new DocumentRejectedException("address", "The address redirected to something that is not a web page.", next.Scheme);
    }

    /// <summary>The body of a final answer within the size limit (<paramref name="token"/> carries the time limit).</summary>
    private async Task<WebResponse> ReadAsync(HttpResponseMessage response, Uri address, CancellationToken token)
    {
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            throw new DocumentRejectedException("not_found", "Nothing was found at the address.", address.ToString());
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new DocumentRejectedException("network", $"The address answered {(int)response.StatusCode}.", ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (response.Content.Headers.ContentLength > DocumentLimits.WebBytesMax)
        {
            throw new DocumentRejectedException("size", "The page is too large.", address.ToString());
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var body = new MemoryStream();
        var buffer = new byte[81_920];
        int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (body.Length + read > DocumentLimits.WebBytesMax)
            {
                throw new DocumentRejectedException("size", "The page is too large.", address.ToString());
            }

            body.Write(buffer, 0, read);
        }

        var type = response.Content.Headers.ContentType;
        Logger.LogInformation("Document fetched: {Bytes} bytes of {Type} from {Host}", body.Length, type?.MediaType ?? "-", address.Host);
        return new WebResponse(body.ToArray(), type?.MediaType?.ToLowerInvariant() ?? string.Empty, type?.CharSet?.Trim('"') ?? string.Empty, address);
    }
}
