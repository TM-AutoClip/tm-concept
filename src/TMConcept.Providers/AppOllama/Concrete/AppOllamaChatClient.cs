using Microsoft.Extensions.Logging;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Chat;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Providers.Endpoints;

namespace TMConcept.Providers.AppOllama;

/// <summary>
/// A chat with a model of the app's own Ollama copy: the server is started first when it does not run (only the app's own server, at the
/// address it got), then the call goes through <see cref="OllamaChatClient"/> exactly as for an Ollama the user added (context as
/// <c>num_ctx</c>, a schema as <c>format</c>, <c>think</c> off by default for a thinking model so a JSON step gets its JSON, images, the
/// <c>keep_alive</c>). A server that cannot start is a <see cref="ProviderException"/> ("app_ollama") with the reason. A prompt larger than the
/// model's context points at a larger model in Settings → Local AI (an app model's context is fixed by the catalogue, not set under API keys).
/// </summary>
public sealed class AppOllamaChatClient(CatalogModel model, AppModelInfo? info, IAppOllamaServer server, AppOllamaApi api, ILogger logger) : IChatClient
{
    /// <summary>What a prompt too large for an app model's context can do.</summary>
    public const string LargerModelHint = "Pick a model with a larger context in Settings → Local AI (or an online model for this step).";

    /// <summary>The stable id the app's server has as an endpoint (it is never saved among the user's servers).</summary>
    public static readonly Guid EndpointId = new("6f1c2b7e-5a3d-4e0b-9c41-0a1b2c3d4e5f");

    /// <inheritdoc />
    public CatalogModel Model { get; } = model;

    /// <inheritdoc />
    public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Uri root;
        try
        {
            root = await server.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AppOllamaException ex)
        {
            logger.LogError(ex, "{Server} could not be started for {Model}", AppOllamaDefaults.Name, Model.Id);
            throw new ProviderException(ex.Message, ex) { Code = "app_ollama" };
        }

        var endpoint = Endpoint(root);
        var saved = Saved(Model, info);
        return await new OllamaChatClient(endpoint, saved, Model, string.Empty, api.Ollama, api.Servers, logger, LargerModelHint).SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The app's server as an Ollama endpoint at <paramref name="root"/> (in memory only).</summary>
    public static LocalEndpoint Endpoint(Uri root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new LocalEndpoint
        {
            Id = EndpointId,
            Name = AppOllamaDefaults.Name,
            BaseUrl = LocalServerAddress.Saved(root),
            Kind = EndpointKind.Ollama,
        };
    }

    /// <summary>The model's limits and flags as an Ollama chat needs them: a JSON schema always (Ollama's grammar), thinking off.</summary>
    public static EndpointModel Saved(CatalogModel model, AppModelInfo? info)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new EndpointModel
        {
            Id = model.Id,
            Name = model.Name,
            ContextTokens = model.ContextTokens,
            MaxOutputTokens = model.MaxOutputTokens,
            Vision = model.Capabilities.HasFlag(ModelCapability.Vision),
            JsonSchema = true,
            CanThink = info?.CanThink ?? false,
            Think = false,
            SizeBytes = info?.Bytes ?? 0,
        };
    }
}
