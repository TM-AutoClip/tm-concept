using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Providers;
using TMConcept.Core.Security;

namespace TMConcept.Providers.Common;

/// <summary>
/// Sends provider requests with the shared rules: one retry after a 429 when the provider asks to wait at most a minute; network and
/// timeout errors become <see cref="ProviderException"/>; a long answer is read line by line as it streams (<see cref="StreamAsync"/>); a
/// failed answer becomes one with a code by status and a short detail where the key and anything key-like are masked. The key is never logged.
/// </summary>
public sealed class ProviderHttp(ProviderTransport transport, ILogger<ProviderHttp> logger) : IDisposable
{
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);
    private readonly HttpClient _http = new(transport.CreateHandler(), disposeHandler: true) { Timeout = transport.Timeout };

    /// <summary>Sends; <paramref name="create"/> builds a new request for every try.</summary>
    public Task<ProviderReply> SendAsync(Func<HttpRequestMessage> create, string providerName, string apiKey, CancellationToken cancellationToken) =>
        SendAsync(create, providerName, apiKey, logSuccess: true, cancellationToken);

    /// <summary>
    /// Sends a status poll (one a second while a job waits): a successful answer is not logged, the caller writes one line for the whole
    /// wait; a failure is logged as for <see cref="SendAsync(Func{HttpRequestMessage}, string, string, CancellationToken)"/>.
    /// </summary>
    public Task<ProviderReply> PollAsync(Func<HttpRequestMessage> create, string providerName, string apiKey, CancellationToken cancellationToken) =>
        SendAsync(create, providerName, apiKey, logSuccess: false, cancellationToken);

    /// <summary>
    /// Sends a streamed request (server-sent events) and hands every line of the answer to <paramref name="line"/> as it arrives, so a long
    /// answer is never cut by one fixed time limit: the answer must start within <see cref="ProviderTransport.Timeout"/>, then a silence
    /// longer than <see cref="ProviderTransport.StreamIdle"/> or a whole answer longer than <see cref="ProviderTransport.StreamLimit"/> ends it
    /// with <c>timeout</c>, a broken connection with <c>network</c> (both carry the request id; the caller adds the usage it has read). A
    /// failed status is read whole and handled as in <see cref="SendAsync(Func{HttpRequestMessage}, string, string, CancellationToken)"/>.
    /// </summary>
    public async Task<StreamReply> StreamAsync(Func<HttpRequestMessage> create, string providerName, string apiKey, Action<string> line,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(line);
        var (request, response, requestId, operation, watch) = await OpenAsync(create, providerName, apiKey, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        using (request)
        using (response)
        {
            long characters = 0;
            long first = -1;
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
                using var reader = new StreamReader(body, System.Text.Encoding.UTF8);
                while (true)
                {
                    // One timer for both limits: the silence allowed now, but never past the end of the whole answer's time.
                    var left = transport.StreamLimit - watch.Elapsed;
                    limit.CancelAfter(left >= transport.StreamIdle ? transport.StreamIdle : left > TimeSpan.Zero ? left : TimeSpan.FromMilliseconds(1));
                    var text = await reader.ReadLineAsync(limit.Token).ConfigureAwait(false);
                    if (text is null)
                    {
                        break;
                    }

                    if (first < 0)
                    {
                        first = watch.ElapsedMilliseconds;
                    }

                    characters += text.Length + 1;
                    line(text);
                }
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("{Provider} {Operation}: the stream stopped after {Ms} ms ({Kilobytes} KB read, request {RequestId})", providerName, operation,
                    watch.ElapsedMilliseconds, characters / 1024, requestId.Length > 0 ? requestId : "-");
                throw new ProviderException($"{providerName} stopped sending the answer.", ex) { Code = "timeout", RequestId = requestId };
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException)
            {
                logger.LogWarning(ex, "{Provider} {Operation}: the stream broke after {Ms} ms ({Kilobytes} KB read, request {RequestId})", providerName, operation,
                    watch.ElapsedMilliseconds, characters / 1024, requestId.Length > 0 ? requestId : "-");
                throw new ProviderException($"{providerName}: the connection broke during the answer.", ex) { Code = "network", RequestId = requestId };
            }

            logger.LogInformation("{Provider} {Operation}: HTTP {Status} streamed in {Ms} ms (first line after {First} ms, {Kilobytes} KB, request {RequestId})",
                providerName, operation, (int)response.StatusCode, watch.ElapsedMilliseconds, first, characters / 1024, requestId.Length > 0 ? requestId : "-");
            return new StreamReply(requestId, characters, first, response.Content.Headers.ContentType?.MediaType ?? string.Empty);
        }
    }

    private async Task<ProviderReply> SendAsync(Func<HttpRequestMessage> create, string providerName, string apiKey, bool logSuccess,
        CancellationToken cancellationToken)
    {
        var (request, response, requestId, operation, watch) = await OpenAsync(create, providerName, apiKey, HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);
        using (request)
        using (response)
        {
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (logSuccess)
            {
                logger.LogInformation("{Provider} {Operation}: HTTP {Status} in {Ms} ms ({Kilobytes} KB, request {RequestId})", providerName, operation,
                    (int)response.StatusCode, watch.ElapsedMilliseconds, body.Length / 1024, requestId.Length > 0 ? requestId : "-");
            }

            return new ProviderReply(body, response.Content.Headers.ContentType?.MediaType ?? string.Empty, requestId);
        }
    }

    /// <summary>
    /// Sends until a successful status (one retry after a 429 when the provider asks to wait at most a minute) and returns the open response
    /// with its request (the caller disposes both); a failed status, a network error and a timeout become a <see cref="ProviderException"/>.
    /// </summary>
    private async Task<(HttpRequestMessage Request, HttpResponseMessage Response, string RequestId, string Operation, Stopwatch Watch)> OpenAsync(
        Func<HttpRequestMessage> create, string providerName, string apiKey, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(create);
        for (var attempt = 1; ; attempt++)
        {
            var request = create();
            var operation = request.Method + " " + (request.RequestUri?.AbsolutePath ?? string.Empty);
            var watch = Stopwatch.StartNew();
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, completion, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                request.Dispose();
                logger.LogWarning(ex, "{Provider} {Operation}: network error after {Ms} ms", providerName, operation, watch.ElapsedMilliseconds);
                throw new ProviderException($"{providerName} could not be reached.", ex) { Code = "network" };
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                request.Dispose();
                logger.LogWarning("{Provider} {Operation}: the request timed out after {Ms} ms", providerName, operation, watch.ElapsedMilliseconds);
                throw new ProviderException($"{providerName} did not answer in time.", ex) { Code = "timeout" };
            }
            catch
            {
                request.Dispose();
                throw;
            }

            var requestId = RequestId(response);
            if (response.IsSuccessStatusCode)
            {
                return (request, response, requestId, operation, watch);
            }

            using (request)
            using (response)
            {
                var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt == 1 && wait <= MaxRetryAfter)
                {
                    logger.LogWarning("{Provider} {Operation}: HTTP 429 after {Ms} ms, retrying once in {Seconds} s", providerName, operation,
                        watch.ElapsedMilliseconds, wait.TotalSeconds);
                    await transport.Delay(wait, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                logger.LogInformation("{Provider} {Operation}: HTTP {Status} in {Ms} ms", providerName, operation, (int)response.StatusCode,
                    watch.ElapsedMilliseconds);
                throw Failure(providerName, (int)response.StatusCode, System.Text.Encoding.UTF8.GetString(body), requestId, apiKey);
            }
        }
    }

    /// <summary>
    /// A failed answer as an exception (also used for errors reported inside a stream or inside a 200 body); <paramref name="charged"/> is
    /// the usage the provider already billed, when the failing answer reported one.
    /// </summary>
    public ProviderException Failure(string providerName, int status, string body, string requestId, string apiKey, Core.Chat.ChatUsage? charged = null)
    {
        var detail = Redactor.Redact(Detail(body), [apiKey]);
        if (detail.Length > 300)
        {
            detail = detail[..300] + "…";
        }

        var code = status switch
        {
            400 when detail.Contains("credit balance", StringComparison.OrdinalIgnoreCase) => "billing",
            400 => "bad_request",
            401 => "auth",
            402 => "billing",
            403 => "forbidden",
            404 => "model",
            413 => "too_large",
            429 => "rate",
            >= 500 => "busy",
            _ => "http",
        };
        logger.LogWarning("{Provider}: HTTP {Status} ({Code}), request {RequestId}: {Detail}", providerName, status, code, requestId.Length > 0 ? requestId : "-", detail);
        return new ProviderException($"{providerName}: HTTP {status}. {detail}".Trim())
        {
            Code = code,
            Status = status,
            RequestId = requestId,
            ChargedUsage = charged is { Prompt: > 0 } or { Output: > 0 } ? charged : null,
        };
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private static string RequestId(HttpResponseMessage response)
    {
        foreach (var name in new[] { "request-id", "x-request-id", "x-fal-request-id" })
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                return values.FirstOrDefault() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string Detail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            var node = JsonNode.Parse(body);
            return Field(node, "error", "message") ?? Field(node, "error") ?? Field(node, "detail") ?? Field(node, "message") ?? body;
        }
        catch (System.Text.Json.JsonException)
        {
            return body;
        }
    }

    private static string? Field(JsonNode? node, string name, string? inner = null)
    {
        if (node is not JsonObject obj || !obj.TryGetPropertyValue(name, out var value) || value is null)
        {
            return null;
        }

        if (inner is null)
        {
            return value is JsonValue ? value.ToString() : value.ToJsonString();
        }

        return value is JsonObject child && child.TryGetPropertyValue(inner, out var nested) && nested is not null ? nested.ToString() : null;
    }
}
