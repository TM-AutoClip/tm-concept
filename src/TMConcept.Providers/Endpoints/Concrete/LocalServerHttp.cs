using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Endpoints;

/// <summary>
/// The HTTP calls of Ollama's and LM Studio's own APIs. A read (<see cref="GetAsync"/>) is quiet: it is also how a server's kind is found
/// and how an unload is watched (several a second), so a 404 or a refused connection is an answer, not a warning. A change (load, unload,
/// chat) goes through <see cref="ProviderHttp"/> and fails with a <see cref="Core.Providers.ProviderException"/> by status. The token goes
/// in a bearer header only when one is set and is never logged.
/// </summary>
public sealed class LocalServerHttp(EndpointTransport transport, ProviderHttp http) : IDisposable
{
    private readonly HttpClient _quiet = new(transport.Http.CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>The local server transport (waits, timeouts).</summary>
    public EndpointTransport Transport => transport;

    /// <summary>The shared provider HTTP layer (chat calls).</summary>
    public ProviderHttp Http => http;

    /// <summary>
    /// <c>GET</c> within <paramref name="wait"/> (default: the probe's wait): the status (0 = no connection or no answer in time) and the
    /// body read as JSON (null when it is not JSON).
    /// </summary>
    public async Task<(int Status, JsonNode? Body)> GetAsync(Uri address, string token, CancellationToken cancellationToken, TimeSpan? wait = null)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(wait ?? transport.ProbeTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            Authorize(request, token);
            using var response = await _quiet.SendAsync(request, limit.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
            JsonNode? body;
            try
            {
                body = text.Length > 0 ? JsonNode.Parse(text) : null;
            }
            catch (JsonException)
            {
                body = null;
            }

            return ((int)response.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return (0, null);
        }
    }

    /// <summary><c>POST</c> of a JSON body; the answer read as JSON (null when empty or not JSON). Fails by status.</summary>
    public async Task<JsonNode?> PostAsync(Uri address, JsonObject body, string token, string serverName, bool quiet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var json = body.ToJsonString();
        HttpRequestMessage Create()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            Authorize(request, token);
            return request;
        }

        var reply = quiet
            ? await http.PollAsync(Create, serverName, token, cancellationToken).ConfigureAwait(false)
            : await http.SendAsync(Create, serverName, token, cancellationToken).ConfigureAwait(false);
        try
        {
            return reply.Body.Length > 0 ? JsonNode.Parse(reply.Text) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Waits between two readings of an unload (instant in tests).</summary>
    public Task PollDelayAsync(CancellationToken cancellationToken) => transport.Http.Delay(transport.UnloadPoll, cancellationToken);

    /// <summary>The bearer header of a set token.</summary>
    public static void Authorize(HttpRequestMessage request, string token)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _quiet.Dispose();
}
