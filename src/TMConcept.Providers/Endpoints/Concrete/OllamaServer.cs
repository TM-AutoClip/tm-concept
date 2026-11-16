using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Providers;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Endpoints;

/// <summary>
/// Ollama's own API (<c>http://127.0.0.1:11434</c> by default, no key): <c>GET /api/version</c> tells it apart, <c>GET /api/tags</c> and
/// <c>POST /api/show</c> list the installed models with their trained context and capabilities (cloud models and embedding-only models are
/// left out), <c>GET /api/ps</c> gives the loaded ones with their share on the graphics card (<c>size_vram</c> of <c>size</c>), and a request
/// with <c>keep_alive: 0</c> unloads a model; the unload is confirmed by reading <c>/api/ps</c> until the model is gone.
/// </summary>
public sealed class OllamaServer(LocalServerHttp http, ILogger logger)
{
    private const double Gibibyte = 1024.0 * 1024 * 1024;

    /// <summary>Name used in logs and errors when the user gave none.</summary>
    public const string DefaultName = "Ollama";

    /// <summary>
    /// Ollama's version at <paramref name="root"/> (null when it is not Ollama) and the HTTP status of the question (0: nothing answers at
    /// this address, so no other probe is worth sending).
    /// </summary>
    public async Task<(int Status, string? Version)> VersionAsync(Uri root, string token, CancellationToken cancellationToken, TimeSpan? wait = null)
    {
        var (status, body) = await http.GetAsync(new Uri(root, "api/version"), token, cancellationToken, wait).ConfigureAwait(false);
        return (status, status == 200 && body is JsonObject obj && JsonRead.Text(obj["version"]) is { Length: > 0 } version ? version : null);
    }

    /// <summary>The installed text models; throws <see cref="ProviderException"/> when the list cannot be read.</summary>
    public async Task<List<ListedModel>> ListAsync(Uri root, string token, CancellationToken cancellationToken)
    {
        var (status, body) = await http.GetAsync(new Uri(root, "api/tags"), token, cancellationToken).ConfigureAwait(false);
        if (status != 200 || body?["models"] is not JsonArray items)
        {
            throw new ProviderException($"Ollama: the model list could not be read (HTTP {status}).")
            {
                Code = status switch { 0 => "network", 401 or 403 => "auth", _ => "unreadable" },
                Status = status,
            };
        }

        var models = new List<ListedModel>();
        foreach (var item in items.OfType<JsonObject>())
        {
            var id = JsonRead.Text(item["name"]) is { Length: > 0 } name ? name : JsonRead.Text(item["model"]);
            if (id.Length == 0 || IsCloud(item, id) || models.Any(m => Same(m.Id, id)))
            {
                continue;
            }

            var show = await ShowAsync(root, token, id, cancellationToken).ConfigureAwait(false);
            var capabilities = (show?["capabilities"] as JsonArray)?.Select(c => JsonRead.Text(c)).ToList();
            if (capabilities is { Count: > 0 } && !capabilities.Contains("completion"))
            {
                // An embedding-only model (or another kind) writes no text.
                continue;
            }

            var trained = TrainedContext(show);
            models.Add(new ListedModel(id, trained > 0 ? Math.Min(trained, EndpointDefaults.ServerContextTokens) : EndpointDefaults.ServerContextTokens,
                Vision: capabilities?.Contains("vision") ?? false,
                CanThink: capabilities?.Contains("thinking") ?? false,
                JsonSchema: true,
                SizeBytes: JsonRead.Long(item["size"])));
        }

        logger.LogInformation("Ollama: {Count} text model(s) installed", models.Count);
        return models;
    }

    /// <summary>The models in memory now with their share on the graphics card; empty when Ollama does not answer.</summary>
    public async Task<List<LoadedServerModel>> LoadedAsync(Uri root, string token, CancellationToken cancellationToken)
    {
        var (status, body) = await http.GetAsync(new Uri(root, "api/ps"), token, cancellationToken).ConfigureAwait(false);
        if (status != 200 || body?["models"] is not JsonArray items)
        {
            return [];
        }

        return items.OfType<JsonObject>()
            .Select(m => new LoadedServerModel(JsonRead.Text(m["name"]) is { Length: > 0 } name ? name : JsonRead.Text(m["model"]), JsonRead.Long(m["size"]),
                JsonRead.Long(m["size_vram"]), JsonRead.Long(m["context_length"])))
            .Where(m => m.Id.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Writes one log line on where <paramref name="modelId"/> sits now: all on the graphics card, or how much went to main memory (Ollama
    /// moves what does not fit there by itself: slower). Never throws (except cancellation).
    /// </summary>
    public async Task ReportAsync(Uri root, string token, string modelId, CancellationToken cancellationToken)
    {
        var loaded = (await LoadedAsync(root, token, cancellationToken).ConfigureAwait(false)).FirstOrDefault(m => Same(m.Id, modelId));
        if (loaded is not { HasMemory: true })
        {
            return;
        }

        if (loaded.MainMemoryBytes > 0)
        {
            logger.LogInformation(
                "Ollama {Model}: {Gpu:0.0} GiB on the graphics card, {Ram:0.0} GiB in main memory ({Share:0}% on the card, context {Context}); the part in main memory runs slower",
                loaded.Id, loaded.VideoMemoryBytes / Gibibyte, loaded.MainMemoryBytes / Gibibyte, 100.0 * loaded.VideoMemoryBytes / loaded.TotalBytes,
                loaded.ContextTokens);
        }
        else
        {
            logger.LogInformation("Ollama {Model}: {Gpu:0.0} GiB, all on the graphics card (context {Context})", loaded.Id, loaded.VideoMemoryBytes / Gibibyte,
                loaded.ContextTokens);
        }
    }

    /// <summary>
    /// Unloads <paramref name="modelId"/> (<c>keep_alive: 0</c>) and reads <c>/api/ps</c> until it is gone (at most
    /// <see cref="EndpointTransport.UnloadTimeout"/>). True when it is gone (or was not loaded); false with a log line when Ollama refused or
    /// still holds it.
    /// </summary>
    public async Task<bool> UnloadAsync(Uri root, string token, string modelId, string serverName, CancellationToken cancellationToken)
    {
        if (await IsGoneAsync(root, token, modelId, cancellationToken).ConfigureAwait(false))
        {
            // Not in memory (Ollama's own idle time already freed it): nothing to ask.
            return true;
        }

        try
        {
            await http.PostAsync(new Uri(root, "api/generate"), new JsonObject { ["model"] = modelId, ["keep_alive"] = 0 }, token, serverName, quiet: true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderException ex) when (ex.Status == 404)
        {
            // Not installed (any more): nothing of it can be in memory.
            return true;
        }
        catch (ProviderException ex)
        {
            logger.LogWarning("Ollama: {Model} could not be unloaded ({Code})", modelId, ex.Code);
            return false;
        }

        var tries = Math.Max(1, (int)(http.Transport.UnloadTimeout / http.Transport.UnloadPoll));
        for (var i = 0; i < tries; i++)
        {
            if (await IsGoneAsync(root, token, modelId, cancellationToken).ConfigureAwait(false))
            {
                logger.LogInformation("Ollama: {Model} unloaded", modelId);
                return true;
            }

            await http.PollDelayAsync(cancellationToken).ConfigureAwait(false);
        }

        logger.LogWarning("Ollama: {Model} still shows as loaded {Seconds} s after the unload request", modelId, http.Transport.UnloadTimeout.TotalSeconds);
        return false;
    }

    /// <summary>Whether <c>/api/ps</c> answers without <paramref name="modelId"/> (no answer is not "gone").</summary>
    private async Task<bool> IsGoneAsync(Uri root, string token, string modelId, CancellationToken cancellationToken)
    {
        var (status, body) = await http.GetAsync(new Uri(root, "api/ps"), token, cancellationToken).ConfigureAwait(false);
        return status == 200 && body?["models"] is JsonArray loaded
            && !loaded.OfType<JsonObject>().Any(m => Same(JsonRead.Text(m["name"]), modelId) || Same(JsonRead.Text(m["model"]), modelId));
    }

    /// <summary>Whether two Ollama model names are the same model (<c>llama3.2</c> is <c>llama3.2:latest</c>).</summary>
    public static bool Same(string a, string b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);

    private async Task<JsonNode?> ShowAsync(Uri root, string token, string id, CancellationToken cancellationToken)
    {
        try
        {
            return await http.PostAsync(new Uri(root, "api/show"), new JsonObject { ["model"] = id }, token, DefaultName, quiet: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ProviderException ex)
        {
            // An old Ollama or a broken entry: the model is listed with the defaults.
            logger.LogInformation("Ollama: no details for {Model} ({Code})", id, ex.Code);
            return null;
        }
    }

    private static long TrainedContext(JsonNode? show)
    {
        if (show?["model_info"] is not JsonObject info)
        {
            return 0;
        }

        var architecture = JsonRead.Text(info["general.architecture"]);
        if (architecture.Length > 0 && JsonRead.Long(info[architecture + ".context_length"]) is > 0 and var context)
        {
            return context;
        }

        return info.Where(p => p.Key.EndsWith(".context_length", StringComparison.Ordinal)).Select(p => JsonRead.Long(p.Value)).FirstOrDefault(v => v > 0);
    }

    private static bool IsCloud(JsonObject item, string id) =>
        item["remote_host"] is not null || item["remote_model"] is not null
        || id.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase) || id.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase);

    private static string Full(string name) => name.Contains(':', StringComparison.Ordinal) ? name : name + ":latest";
}
