using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;

namespace TMConcept.Documents.Web;

/// <summary>
/// The app's guarded HTTP for documents and research (0.1.21, shared since 0.1.22): only http and https, and only to the public internet
/// (<see cref="NetworkAddresses"/>: an address on this computer or the local network is refused, also when a redirect leads there; the real
/// transport checks the resolved addresses too); one time limit for the whole request, the body included; a size limit
/// (<see cref="DocumentLimits.WebBytesMax"/>, also when the server does not say the size); at most <see cref="RedirectsMax"/> redirects for a
/// GET and none for a POST; a User-Agent that names the app, its version and its public project page (as the Wikimedia policy asks), nothing
/// about the user. Kind to the free services: one request at a time per host across the whole app, and a "wait" answer (429, or a
/// Wikimedia "maxlag" answer) is tried once more after the wait the service asks when that wait is at most <see cref="RetryWaitMax"/>.
/// Extra headers (a keyed search's key) are sent but never logged: the log holds the host, the status and the size only. 404 is "not
/// found", 401/403 of a request with a key "key", a wait answer that stays "rate", any other failure says the system's reason
/// (<see cref="DocumentRejectedException"/>).
/// </summary>
public sealed class SafeHttp(DocumentTransport transport, ILogger logger)
{
    /// <summary>Redirects followed at most.</summary>
    public const int RedirectsMax = 5;

    /// <summary>The app's public project page (named in the User-Agent; a project address, nothing about the user).</summary>
    public const string ProjectAddress = "https://github.com/TM-AutoClip/tm-concept";

    /// <summary>The longest wait a "wait" answer is honoured with before the one more try (a longer one is reported at once).</summary>
    public static readonly TimeSpan RetryWaitMax = TimeSpan.FromSeconds(10);

    /// <summary>The wait when a "wait" answer does not say how long.</summary>
    private static readonly TimeSpan RetryWaitDefault = TimeSpan.FromSeconds(2);

    /// <summary>One request at a time per host, for every SafeHttp of the app.</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The User-Agent of every request.</summary>
    public static string UserAgent { get; } = "TM-Concept/" + (typeof(SafeHttp).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0")
        + " (+" + ProjectAddress + "; open-source desktop app)";

    /// <summary>Refuses an address whose host is this computer or the local network (<see cref="NetworkAddresses.IsLocalHost"/>).</summary>
    public static void RefuseLocal(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (NetworkAddresses.IsLocalHost(address.Host))
        {
            throw new DocumentRejectedException("private", "The address is on this computer or the local network.", address.Host);
        }
    }

    /// <summary>Fetches <paramref name="address"/> within the limits, following public redirects.</summary>
    public Task<WebResponse> GetAsync(Uri address, CancellationToken cancellationToken) => SendAsync(HttpMethod.Get, address, null, null, cancellationToken);

    /// <summary>Posts <paramref name="json"/> to <paramref name="address"/> with <paramref name="headers"/> (never logged); a redirect is refused.</summary>
    public Task<WebResponse> PostJsonAsync(Uri address, string json, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(headers);
        return SendAsync(HttpMethod.Post, address, json, headers, cancellationToken);
    }

    /// <summary>
    /// How long a "wait" answer asks to wait (its Retry-After, else a short default); null when <paramref name="response"/> is no such
    /// answer: a 429, or a MediaWiki answer that says its database lags behind (the "maxlag" error comes with status 200).
    /// </summary>
    private static TimeSpan? Throttled(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests && !response.Headers.Contains("X-Database-Lag"))
        {
            return null;
        }

        var asked = response.Headers.RetryAfter is { } after
            ? after.Delta ?? (after.Date is { } date ? date - DateTimeOffset.UtcNow : RetryWaitDefault)
            : RetryWaitDefault;
        return asked < TimeSpan.Zero ? TimeSpan.Zero : asked;
    }

    private async Task<WebResponse> SendAsync(HttpMethod method, Uri address, string? json, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(transport.TimeLimit);
        using var client = new HttpClient(transport.CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        var current = address;
        var hops = 0;
        var retried = false;
        try
        {
            while (true)
            {
                if (current.Scheme != Uri.UriSchemeHttps && current.Scheme != Uri.UriSchemeHttp)
                {
                    throw new DocumentRejectedException("address", "The address is not a web page.", current.Scheme);
                }

                RefuseLocal(current);
                var gate = Hosts.GetOrAdd(current.Host, _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(limit.Token).ConfigureAwait(false);
                TimeSpan wait;
                try
                {
                    using var request = new HttpRequestMessage(method, current);
                    request.Headers.UserAgent.ParseAdd(UserAgent);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.5));
                    foreach (var (name, value) in headers ?? new Dictionary<string, string>())
                    {
                        request.Headers.TryAddWithoutValidation(name, value);
                    }

                    if (json is not null)
                    {
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    }

                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
                    if (Redirect(response, current) is { } next)
                    {
                        if (method != HttpMethod.Get || hops >= RedirectsMax)
                        {
                            throw new DocumentRejectedException("network", "The address redirected too many times.", address.Host);
                        }

                        hops++;
                        current = next;
                        continue;
                    }

                    if (Throttled(response) is not { } asked)
                    {
                        return await ReadAsync(response, current, headers is { Count: > 0 }, limit.Token).ConfigureAwait(false);
                    }

                    if (retried || asked > RetryWaitMax)
                    {
                        logger.LogWarning("Fetch from {Host}: the service asks to wait ({Seconds} s)", current.Host, Math.Round(asked.TotalSeconds));
                        throw new DocumentRejectedException("rate", "The service asks to wait before the next request.", current.Host);
                    }

                    wait = asked;
                }
                finally
                {
                    gate.Release();
                }

                retried = true;
                logger.LogInformation("Fetch from {Host}: the service asks to wait {Seconds} s; one more try", current.Host, Math.Round(wait.TotalSeconds, 1));
                await Task.Delay(wait, limit.Token).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException ex) when (ex.InnerException is DocumentRejectedException rejected)
        {
            logger.LogWarning("Fetch refused: {Host} is on this computer or the local network", current.Host);
            throw rejected;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Fetch from {Host} failed", current.Host);
            throw new DocumentRejectedException("network", "The address could not be reached.", ex.Message, ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Fetch from {Host} timed out", current.Host);
            throw new DocumentRejectedException("network", "The address did not answer in time.",
                string.Create(CultureInfo.InvariantCulture, $"{transport.TimeLimit.TotalSeconds:0.#} s"), ex);
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
    private async Task<WebResponse> ReadAsync(HttpResponseMessage response, Uri address, bool keyed, CancellationToken token)
    {
        var status = (int)response.StatusCode;
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            throw new DocumentRejectedException("not_found", "Nothing was found at the address.", address.ToString());
        }

        if (keyed && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogWarning("Fetch from {Host} was refused ({Status})", address.Host, status);
            throw new DocumentRejectedException("key", $"The address answered {status}.", status.ToString(CultureInfo.InvariantCulture));
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new DocumentRejectedException("network", $"The address answered {status}.", status.ToString(CultureInfo.InvariantCulture));
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
        logger.LogInformation("Fetched {Bytes} bytes of {Type} from {Host}", body.Length, type?.MediaType ?? "-", address.Host);
        return new WebResponse(body.ToArray(), type?.MediaType?.ToLowerInvariant() ?? string.Empty, type?.CharSet?.Trim('"') ?? string.Empty, address);
    }
}
