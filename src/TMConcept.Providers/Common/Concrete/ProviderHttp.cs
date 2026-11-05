using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Providers;
using TMConcept.Core.Security;

namespace TMConcept.Providers.Common;

/// <summary>
/// Sends provider requests with the shared rules: one retry after a 429 when the provider asks to wait at most a minute; network and
/// timeout errors become <see cref="ProviderException"/>; a failed answer becomes one with a code by status and a short detail where the key
/// and anything key-like are masked. The key is never logged.
/// </summary>
public sealed class ProviderHttp(ProviderTransport transport, ILogger<ProviderHttp> logger) : IDisposable
{
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);
    private readonly HttpClient _http = new(transport.CreateHandler(), disposeHandler: true) { Timeout = transport.Timeout };

    /// <summary>Sends; <paramref name="create"/> builds a new request for every try.</summary>
    public async Task<ProviderReply> SendAsync(Func<HttpRequestMessage> create, string providerName, string apiKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(create);
        for (var attempt = 1; ; attempt++)
        {
            using var request = create();
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "{Provider}: network error", providerName);
                throw new ProviderException($"{providerName} could not be reached.", ex) { Code = "network" };
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("{Provider}: the request timed out", providerName);
                throw new ProviderException($"{providerName} did not answer in time.", ex) { Code = "timeout" };
            }

            using (response)
            {
                var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                var requestId = RequestId(response);
                if (response.IsSuccessStatusCode)
                {
                    return new ProviderReply(body, response.Content.Headers.ContentType?.MediaType ?? string.Empty, requestId);
                }

                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt == 1 && wait <= MaxRetryAfter)
                {
                    logger.LogWarning("{Provider}: 429, retrying once after {Seconds} s", providerName, wait.TotalSeconds);
                    await transport.Delay(wait, cancellationToken).ConfigureAwait(false);
                    continue;
                }

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
