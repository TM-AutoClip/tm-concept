using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;

namespace TMConcept.Documents.Web;

/// <summary>
/// The common part of fetching a document: only http and https; the app's own HTTP layer (<see cref="DocumentTransport"/>) with a time limit
/// (<see cref="DocumentLimits.WebSecondsMax"/>) and a size limit (<see cref="DocumentLimits.WebBytesMax"/>, also when the server does not say
/// the size); a generic User-Agent that names the app and its version, nothing about the user; 404 is "not found", any other failure says the
/// system's reason (<see cref="DocumentRejectedException"/>).
/// </summary>
public abstract class DocumentFetcherBase(DocumentTransport transport, ILogger logger) : IDocumentFetcher
{
    /// <summary>The User-Agent of every document request.</summary>
    public static string UserAgent { get; } = "TM-Concept/" + (typeof(DocumentFetcherBase).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0")
        + " (open-source desktop app)";

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

    /// <summary>Fetches <paramref name="address"/> within the limits.</summary>
    protected async Task<WebResponse> GetAsync(Uri address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        using var client = new HttpClient(transport.CreateHandler(), disposeHandler: true) { Timeout = TimeSpan.FromSeconds(DocumentLimits.WebSecondsMax) };
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.5));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
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

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var body = new MemoryStream();
            var buffer = new byte[81_920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (body.Length + read > DocumentLimits.WebBytesMax)
                {
                    throw new DocumentRejectedException("size", "The page is too large.", address.ToString());
                }

                body.Write(buffer, 0, read);
            }

            var type = response.Content.Headers.ContentType;
            var final = response.RequestMessage?.RequestUri ?? address;
            logger.LogInformation("Document fetched: {Bytes} bytes of {Type} from {Host}", body.Length, type?.MediaType ?? "-", final.Host);
            return new WebResponse(body.ToArray(), type?.MediaType?.ToLowerInvariant() ?? string.Empty, type?.CharSet?.Trim('"') ?? string.Empty, final);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Document fetch from {Host} failed", address.Host);
            throw new DocumentRejectedException("network", "The address could not be reached.", ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Document fetch from {Host} timed out", address.Host);
            throw new DocumentRejectedException("network", "The address did not answer in time.", $"{DocumentLimits.WebSecondsMax} s", ex);
        }
    }
}
