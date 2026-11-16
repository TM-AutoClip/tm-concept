using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Providers;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Endpoints;

/// <summary>
/// LM Studio's own REST API (0.4 and later, <c>http://127.0.0.1:1234</c> by default; a bearer token when the user turned authentication on):
/// <c>GET /api/v1/models</c> lists the downloaded models (<c>key</c>, <c>type</c>, <c>max_context_length</c>, <c>capabilities</c>) and the
/// loaded ones (<c>loaded_instances</c>); <c>POST /api/v1/models/load</c> loads a model with the context the app chose (a model loaded only on
/// the first chat request gets LM Studio's default context, often 4 096); <c>POST /api/v1/models/unload</c> unloads one instance. LM Studio
/// reports no graphics memory per model, and its REST load has no GPU share setting: it chooses the share itself (its per-model GPU offload
/// setting, or <c>lms load --gpu</c>).
/// </summary>
public sealed class LmStudioServer(LocalServerHttp http, ILogger logger)
{
    /// <summary>Name used in logs and errors when the user gave none.</summary>
    public const string DefaultName = "LM Studio";

    // One load check at a time: two calls of the same model at once (the AI edit while the queue writes) would load it twice.
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Whether <paramref name="root"/> is LM Studio 0.4+ (its model list has <c>models[]</c> with <c>key</c>).</summary>
    public async Task<bool> IsLmStudioAsync(Uri root, string token, CancellationToken cancellationToken, TimeSpan? wait = null)
    {
        var (status, body) = await http.GetAsync(new Uri(root, "api/v1/models"), token, cancellationToken, wait).ConfigureAwait(false);
        return status == 200 && IsList(body);
    }

    /// <summary>The downloaded text models (embedding models left out); throws <see cref="ProviderException"/> when the list cannot be read.</summary>
    public async Task<List<ListedModel>> ListAsync(Uri root, string token, CancellationToken cancellationToken)
    {
        var items = await ModelsAsync(root, token, cancellationToken).ConfigureAwait(false);
        var models = new List<ListedModel>();
        foreach (var item in items)
        {
            var id = JsonRead.Text(item["key"]);
            if (id.Length == 0 || !IsText(item) || models.Any(m => m.Id == id))
            {
                continue;
            }

            var running = Instances(item).Select(i => JsonRead.Long(i["config"]?["context_length"])).DefaultIfEmpty(0).Max();
            var trained = JsonRead.Long(item["max_context_length"]);
            var context = running > 0 ? running : trained > 0 ? Math.Min(trained, EndpointDefaults.ServerContextTokens) : EndpointDefaults.ServerContextTokens;
            models.Add(new ListedModel(id, context,
                Name: JsonRead.Text(item["display_name"]),
                Vision: item["capabilities"]?["vision"] is JsonValue vision && vision.TryGetValue<bool>(out var reads) && reads,
                JsonSchema: true,
                SizeBytes: JsonRead.Long(item["size_bytes"])));
        }

        logger.LogInformation("LM Studio: {Count} text model(s) downloaded", models.Count);
        return models;
    }

    /// <summary>The loaded models with the context each was loaded with (no memory figures: LM Studio reports none); empty when it does not answer.</summary>
    public async Task<List<LoadedServerModel>> LoadedAsync(Uri root, string token, CancellationToken cancellationToken)
    {
        var (status, body) = await http.GetAsync(new Uri(root, "api/v1/models"), token, cancellationToken).ConfigureAwait(false);
        if (status != 200 || !IsList(body))
        {
            return [];
        }

        return ((JsonArray)body!["models"]!).OfType<JsonObject>()
            .SelectMany(m => Instances(m).Select(i => new LoadedServerModel(JsonRead.Text(i["id"]) is { Length: > 0 } id ? id : JsonRead.Text(m["key"]), 0, 0,
                JsonRead.Long(i["config"]?["context_length"]))))
            .ToList();
    }

    /// <summary>
    /// Makes sure <paramref name="modelId"/> is loaded with at least <paramref name="contextTokens"/> of context: an instance with a smaller
    /// context is unloaded and the model is loaded again with the right one. A model LM Studio does not list (typed by hand) or an LM Studio
    /// whose list cannot be read is left to load on the chat request itself. A failed load throws <see cref="ProviderException"/> (LM Studio's
    /// reason in its detail: usually not enough memory).
    /// </summary>
    public async Task EnsureLoadedAsync(Uri root, string token, string modelId, long contextTokens, string serverName, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (status, body) = await http.GetAsync(new Uri(root, "api/v1/models"), token, cancellationToken).ConfigureAwait(false);
            if (status != 200 || !IsList(body) || ((JsonArray)body!["models"]!).OfType<JsonObject>().FirstOrDefault(m => JsonRead.Text(m["key"]) == modelId) is not { } model)
            {
                logger.LogInformation("LM Studio: {Model} is not in its model list (HTTP {Status}); it loads on the request", modelId, status);
                return;
            }

            var instances = Instances(model).ToList();
            if (instances.Any(i => JsonRead.Long(i["config"]?["context_length"]) is var loaded && (loaded == 0 || loaded >= contextTokens)))
            {
                return;
            }

            foreach (var instance in instances)
            {
                logger.LogInformation("LM Studio: {Model} is loaded with a smaller context ({Loaded}); loading it again with {Context}", modelId,
                    JsonRead.Long(instance["config"]?["context_length"]), contextTokens);
                await UnloadInstanceAsync(root, token, JsonRead.Text(instance["id"]), serverName, cancellationToken).ConfigureAwait(false);
            }

            var answer = await http.PostAsync(new Uri(root, "api/v1/models/load"), new JsonObject
            {
                ["model"] = modelId,
                ["context_length"] = contextTokens,
                ["echo_load_config"] = true,
            }, token, serverName, quiet: false, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("LM Studio: {Model} loaded with a context of {Context} tokens in {Seconds} s (instance {Instance})", modelId,
                JsonRead.Long(answer?["load_config"]?["context_length"]) is > 0 and var loadedContext ? loadedContext : contextTokens,
                JsonRead.Decimal(answer?["load_time_seconds"])?.ToString("0.0", CultureInfo.InvariantCulture) ?? "-", JsonRead.Text(answer?["instance_id"]));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Unloads every loaded instance of <paramref name="modelId"/> and reads the list until none is left (at most
    /// <see cref="EndpointTransport.UnloadTimeout"/>). True when none is left (or none was loaded); false with a log line otherwise.
    /// </summary>
    public async Task<bool> UnloadAsync(Uri root, string token, string modelId, string serverName, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tries = Math.Max(1, (int)(http.Transport.UnloadTimeout / http.Transport.UnloadPoll));
            var asked = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < tries; i++)
            {
                var (status, body) = await http.GetAsync(new Uri(root, "api/v1/models"), token, cancellationToken).ConfigureAwait(false);
                if (status != 200 || !IsList(body))
                {
                    logger.LogWarning("LM Studio: the model list could not be read to unload {Model} (HTTP {Status})", modelId, status);
                    return false;
                }

                var instances = ((JsonArray)body!["models"]!).OfType<JsonObject>()
                    .Where(m => JsonRead.Text(m["key"]) == modelId)
                    .SelectMany(Instances)
                    .Select(x => JsonRead.Text(x["id"]))
                    .Where(x => x.Length > 0)
                    .ToList();
                if (instances.Count == 0)
                {
                    if (asked.Count > 0)
                    {
                        logger.LogInformation("LM Studio: {Model} unloaded", modelId);
                    }

                    return true;
                }

                foreach (var instance in instances.Where(asked.Add))
                {
                    if (!await UnloadInstanceAsync(root, token, instance, serverName, cancellationToken).ConfigureAwait(false))
                    {
                        return false;
                    }
                }

                await http.PollDelayAsync(cancellationToken).ConfigureAwait(false);
            }

            logger.LogWarning("LM Studio: {Model} still shows as loaded {Seconds} s after the unload request", modelId, http.Transport.UnloadTimeout.TotalSeconds);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<JsonObject[]> ModelsAsync(Uri root, string token, CancellationToken cancellationToken)
    {
        var (status, body) = await http.GetAsync(new Uri(root, "api/v1/models"), token, cancellationToken).ConfigureAwait(false);
        if (status != 200 || !IsList(body))
        {
            throw new ProviderException($"LM Studio: the model list could not be read (HTTP {status}).")
            {
                Code = status switch { 0 => "network", 401 or 403 => "auth", _ => "unreadable" },
                Status = status,
            };
        }

        return [.. ((JsonArray)body!["models"]!).OfType<JsonObject>()];
    }

    private async Task<bool> UnloadInstanceAsync(Uri root, string token, string instance, string serverName, CancellationToken cancellationToken)
    {
        try
        {
            await http.PostAsync(new Uri(root, "api/v1/models/unload"), new JsonObject { ["instance_id"] = instance }, token, serverName, quiet: true,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ProviderException ex) when (ex.Status == 404)
        {
            // Already gone.
            return true;
        }
        catch (ProviderException ex)
        {
            logger.LogWarning("LM Studio: instance {Instance} could not be unloaded ({Code})", instance, ex.Code);
            return false;
        }
    }

    private static bool IsList(JsonNode? body) =>
        body is JsonObject obj && obj["models"] is JsonArray items && items.OfType<JsonObject>().All(m => m["key"] is not null);

    private static bool IsText(JsonObject model) => JsonRead.Text(model["type"]) is "" or "llm" or "vlm";

    private static IEnumerable<JsonObject> Instances(JsonObject model) => (model["loaded_instances"] as JsonArray)?.OfType<JsonObject>() ?? [];
}
