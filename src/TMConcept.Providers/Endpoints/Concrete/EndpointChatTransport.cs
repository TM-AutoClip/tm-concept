using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Providers.Chat;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Endpoints;

/// <summary>
/// The chat transport of every user-added local server (kind <see cref="ModelProviders.Endpoint"/>; a model's code
/// <c>endpoint:&lt;id&gt;</c> names its server in <see cref="EndpointRegistry"/>) and their connection test: <c>GET {base}/models</c> with a short
/// wait, reading each model's id and, when the server says, its context window (<c>context_length</c> / <c>max_context_window</c> of
/// Lemonade, <c>max_model_len</c> of vLLM, <c>meta.n_ctx_train</c> of llama.cpp). Its own HTTP layer (<see cref="EndpointTransport"/>: long
/// waits). The token is never logged; a failure's detail masks it.
/// </summary>
public sealed class EndpointChatTransport(EndpointRegistry endpoints, EndpointTransport transport, ILoggerFactory loggers)
    : IChatTransport, IEndpointProbe, IDisposable
{
    private readonly ProviderHttp _http = new(transport.Http, loggers.CreateLogger<ProviderHttp>());
    private readonly ILogger _logger = loggers.CreateLogger<EndpointChatTransport>();

    /// <inheritdoc />
    public string Code => ModelProviders.Endpoint;

    /// <inheritdoc />
    public bool KeyRequired => false;

    /// <inheritdoc />
    public IChatClient CreateChat(CatalogModel model, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!EndpointCodes.TryId(model.Provider, out var id) || endpoints.Find(id) is not { } endpoint)
        {
            throw new InvalidOperationException($"The local server of '{model.Id}' is not saved any more.");
        }

        var token = string.IsNullOrEmpty(apiKey) ? endpoint.Token : apiKey;
        var provider = new EndpointProvider(endpoint, endpoint.Models.FirstOrDefault(m => m.Id == model.Id), transport, _http, _logger);
        return provider.CreateChat(model, token);
    }

    /// <inheritdoc />
    public async Task<EndpointListing> ListModelsAsync(string baseUrl, string token, CancellationToken cancellationToken)
    {
        if (!EndpointAddress.TryParse(baseUrl, out var address))
        {
            return new EndpointListing(false, "address", string.Empty, []);
        }

        token ??= string.Empty;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(transport.ProbeTimeout);
        ProviderReply reply;
        try
        {
            reply = await _http.SendAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, new Uri(address, "models"));
                if (token.Length > 0)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                return request;
            }, EndpointProvider.DefaultName, token, limit.Token).ConfigureAwait(false);
        }
        catch (ProviderException ex)
        {
            _logger.LogWarning("Local server model list failed ({Code})", ex.Code);
            return new EndpointListing(false, ex.Code, ex.Message, []);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Local server model list: no answer in {Seconds} s", transport.ProbeTimeout.TotalSeconds);
            return new EndpointListing(false, "timeout", string.Empty, []);
        }

        var models = Read(reply.Text);
        if (models is null)
        {
            _logger.LogWarning("Local server model list: the answer is not a model list");
            return new EndpointListing(false, "unreadable", string.Empty, []);
        }

        _logger.LogInformation("Local server model list: {Count} model(s)", models.Count);
        return models.Count == 0 ? new EndpointListing(false, "empty", string.Empty, []) : new EndpointListing(true, "ok", string.Empty, models);
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>The models of an OpenAI-style list (<c>{"data":[{"id":…}]}</c>, or a bare array); null when it is not one.</summary>
    internal static List<ListedModel>? Read(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        var items = node is JsonObject obj ? obj["data"] as JsonArray : node as JsonArray;
        if (items is null)
        {
            return null;
        }

        return items.OfType<JsonObject>()
            .Select(m => new ListedModel(JsonRead.Text(m["id"]), Context(m)))
            .Where(m => m.Id.Length > 0)
            .DistinctBy(m => m.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static long Context(JsonObject model)
    {
        foreach (var value in new[] { model["context_length"], model["max_context_window"], model["max_model_len"], model["meta"]?["n_ctx_train"] })
        {
            if (JsonRead.Long(value) is > 0 and var tokens)
            {
                return tokens;
            }
        }

        return 0;
    }
}
