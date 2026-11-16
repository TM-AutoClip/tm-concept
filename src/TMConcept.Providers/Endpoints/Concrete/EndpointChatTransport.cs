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
/// Lemonade, <c>max_model_len</c> of vLLM). llama.cpp lists only <c>meta.n_ctx_train</c>, the context the model was trained with (131 072),
/// not the one the server runs it with (<c>-c</c>, often 4 096–8 192), so it is not taken: for a server with one model whose list has no
/// context, <c>GET {root}/props</c> gives llama-server's real one (<c>default_generation_settings.n_ctx</c>); otherwise the editor's default
/// stands. Ollama and LM Studio are found first (<c>/api/version</c>, <c>/api/v1/models</c>) and listed and run through their own API
/// (<see cref="OllamaServer"/>, <see cref="LmStudioServer"/>, <see cref="OllamaChatClient"/>, <see cref="LmStudioChatClient"/>). Its own HTTP layer (<see cref="EndpointTransport"/>: long waits). The token is never logged; a failure's detail masks it.
/// </summary>
public sealed class EndpointChatTransport : IChatTransport, IEndpointProbe, IDisposable
{
    private readonly EndpointRegistry _endpoints;
    private readonly EndpointTransport _transport;
    private readonly ProviderHttp _http;
    private readonly LocalServerHttp _servers;
    private readonly ILogger _logger;

    /// <summary>Over the saved servers and the local server HTTP layer.</summary>
    public EndpointChatTransport(EndpointRegistry endpoints, EndpointTransport transport, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(loggers);
        _endpoints = endpoints;
        _transport = transport;
        _http = new ProviderHttp(transport.Http, loggers.CreateLogger<ProviderHttp>());
        _servers = new LocalServerHttp(transport, _http);
        _logger = loggers.CreateLogger<EndpointChatTransport>();
        Ollama = new OllamaServer(_servers, loggers.CreateLogger<OllamaServer>());
        LmStudio = new LmStudioServer(_servers, loggers.CreateLogger<LmStudioServer>());
    }

    /// <summary>Ollama's own API (model list, memory, unload).</summary>
    public OllamaServer Ollama { get; }

    /// <summary>LM Studio's own API (model list, load, unload).</summary>
    public LmStudioServer LmStudio { get; }

    /// <inheritdoc />
    public string Code => ModelProviders.Endpoint;

    /// <inheritdoc />
    public bool KeyRequired => false;

    /// <inheritdoc />
    public IChatClient CreateChat(CatalogModel model, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!EndpointCodes.TryId(model.Provider, out var id) || _endpoints.Find(id) is not { } endpoint)
        {
            throw new InvalidOperationException($"The local server of '{model.Id}' is not saved any more.");
        }

        var token = string.IsNullOrEmpty(apiKey) ? endpoint.Token : apiKey;
        var saved = endpoint.Models.FirstOrDefault(m => m.Id == model.Id);
        if (endpoint.Kind == EndpointKind.Ollama)
        {
            return new OllamaChatClient(endpoint, saved, model, token, Ollama, _servers, _logger);
        }

        var chat = new EndpointProvider(endpoint, saved, _transport, _http, _logger).CreateChat(model, token);
        return endpoint.Kind == EndpointKind.LmStudio ? new LmStudioChatClient(chat, endpoint, token, LmStudio) : chat;
    }

    /// <inheritdoc />
    public async Task<EndpointListing> ListModelsAsync(string baseUrl, string token, CancellationToken cancellationToken)
    {
        if (!EndpointAddress.TryParse(baseUrl, out var address))
        {
            return new EndpointListing(false, "address", string.Empty, []);
        }

        token ??= string.Empty;
        var root = LocalServerAddress.Root(address);
        var (status, version) = await Ollama.VersionAsync(root, token, cancellationToken).ConfigureAwait(false);
        if (version is not null)
        {
            return await NativeAsync(EndpointKind.Ollama, root, version, () => Ollama.ListAsync(root, token, cancellationToken)).ConfigureAwait(false);
        }

        // Nothing answered at all: the plain list below says so with its own failure (no second probe to wait for).
        if (status != 0 && await LmStudio.IsLmStudioAsync(root, token, cancellationToken).ConfigureAwait(false))
        {
            return await NativeAsync(EndpointKind.LmStudio, root, string.Empty, () => LmStudio.ListAsync(root, token, cancellationToken)).ConfigureAwait(false);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_transport.ProbeTimeout);
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
            _logger.LogWarning("Local server model list: no answer in {Seconds} s", _transport.ProbeTimeout.TotalSeconds);
            return new EndpointListing(false, "timeout", string.Empty, []);
        }

        var models = Read(reply.Text);
        if (models is null)
        {
            _logger.LogWarning("Local server model list: the answer is not a model list");
            return new EndpointListing(false, "unreadable", string.Empty, []);
        }

        if (models.Count == 1 && models[0].ContextTokens == 0 && await ServerContextAsync(address, token, limit.Token).ConfigureAwait(false) is > 0 and var context)
        {
            models[0] = models[0] with { ContextTokens = context };
        }

        _logger.LogInformation("Local server model list: {Count} model(s)", models.Count);
        return models.Count == 0 ? new EndpointListing(false, "empty", string.Empty, []) : new EndpointListing(true, "ok", string.Empty, models);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DetectedServer>> DetectAsync(CancellationToken cancellationToken)
    {
        var found = await Task.WhenAll(_transport.DetectAddresses.Select(a => DetectOneAsync(a, cancellationToken))).ConfigureAwait(false);
        var servers = found.OfType<DetectedServer>().ToList();
        _logger.LogInformation("Local servers on this computer: {Found}", servers.Count == 0
            ? "none found"
            : string.Join(", ", servers.Select(s => s.Kind + " (" + s.ModelCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " model(s))")));
        return servers;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoadedServerModel>> LoadedAsync(LocalEndpoint endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.Kind switch
        {
            EndpointKind.Ollama => await Ollama.LoadedAsync(LocalServerAddress.Root(endpoint), endpoint.Token, cancellationToken).ConfigureAwait(false),
            EndpointKind.LmStudio => await LmStudio.LoadedAsync(LocalServerAddress.Root(endpoint), endpoint.Token, cancellationToken).ConfigureAwait(false),
            _ => [],
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _servers.Dispose();
        _http.Dispose();
    }

    /// <summary>The listing of a server whose own API lists its models (Ollama, LM Studio).</summary>
    private async Task<EndpointListing> NativeAsync(EndpointKind kind, Uri root, string version, Func<Task<List<ListedModel>>> list)
    {
        try
        {
            var models = await list().ConfigureAwait(false);
            _logger.LogInformation("Local server is {Kind} {Version}: {Count} text model(s)", kind, version.Length > 0 ? version : "-", models.Count);
            return new EndpointListing(models.Count > 0, models.Count > 0 ? "ok" : "empty", string.Empty, models, kind, LocalServerAddress.Saved(root), version);
        }
        catch (ProviderException ex)
        {
            _logger.LogWarning("{Kind} model list failed ({Code})", kind, ex.Code);
            return new EndpointListing(false, ex.Code, ex.Message, [], kind, LocalServerAddress.Saved(root), version);
        }
    }

    /// <summary>Ollama or LM Studio at <paramref name="address"/>; null when neither answers in <see cref="EndpointTransport.DetectTimeout"/>.</summary>
    private async Task<DetectedServer?> DetectOneAsync(string address, CancellationToken cancellationToken)
    {
        if (!EndpointAddress.TryParse(address, out var parsed))
        {
            return null;
        }

        var root = LocalServerAddress.Root(parsed);
        try
        {
            var (status, version) = await Ollama.VersionAsync(root, string.Empty, cancellationToken, _transport.DetectTimeout).ConfigureAwait(false);
            if (status == 0)
            {
                return null;
            }

            if (version is not null)
            {
                var models = await Ollama.ListAsync(root, string.Empty, cancellationToken).ConfigureAwait(false);
                return new DetectedServer(EndpointKind.Ollama, LocalServerAddress.Saved(root), version, models.Count);
            }

            if (await LmStudio.IsLmStudioAsync(root, string.Empty, cancellationToken, _transport.DetectTimeout).ConfigureAwait(false))
            {
                var models = await LmStudio.ListAsync(root, string.Empty, cancellationToken).ConfigureAwait(false);
                return new DetectedServer(EndpointKind.LmStudio, LocalServerAddress.Saved(root), string.Empty, models.Count);
            }
        }
        catch (ProviderException ex)
        {
            _logger.LogInformation("A local server answered at {Address} but its models could not be listed ({Code})", LocalServerAddress.Saved(root), ex.Code);
        }

        return null;
    }

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

    /// <summary>
    /// llama-server's running context from <c>GET {root}/props</c> (the address without its <c>/v1</c>); 0 when the server has no such page
    /// (Lemonade, Ollama, LM Studio) or does not answer in the probe's time. Read quietly: a missing page is the usual case, not a warning.
    /// </summary>
    private async Task<long> ServerContextAsync(Uri address, string token, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient(_transport.Http.CreateHandler(), disposeHandler: true) { Timeout = _transport.ProbeTimeout };
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(address, "../props"));
            if (token.Length > 0)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return 0;
            }

            var props = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var context = JsonRead.Long(props?["default_generation_settings"]?["n_ctx"]) is > 0 and var running ? running : JsonRead.Long(props?["n_ctx"]);
            _logger.LogInformation("Local server: llama-server runs its model with a context of {Context} tokens", context);
            return Math.Max(0, context);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return 0;
        }
    }

    private static long Context(JsonObject model)
    {
        foreach (var value in new[] { model["context_length"], model["max_context_window"], model["max_model_len"] })
        {
            if (JsonRead.Long(value) is > 0 and var tokens)
            {
                return tokens;
            }
        }

        return 0;
    }
}
