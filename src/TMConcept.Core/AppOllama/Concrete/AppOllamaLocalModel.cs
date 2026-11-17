using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;

namespace TMConcept.Core.AppOllama;

/// <summary>
/// A model of the app's own Ollama copy as a local model. On the graphics card (when this computer has a usable one) it needs its download
/// plus the KV cache of its context (<see cref="AppModelAdvisor.CardBytes"/>); what does not fit goes to main memory (Ollama places it; the
/// default <see cref="LocalModelBase.PlanMemory"/> plans it as slower, never refused). Loading starts the app's server when it does not run
/// (the model itself loads on its first request). Unloading asks the server to drop it (<c>keep_alive: 0</c>, then <c>/api/ps</c> until it
/// is gone); when the server still holds it, the server is ended instead, which certainly frees the card (it starts again on the next use).
/// </summary>
public sealed class AppOllamaLocalModel(CatalogModel catalog, AppModelInfo info, IAppOllamaServer server, IAppOllamaApi api, bool onCard, ILogger logger)
    : LocalModelBase(catalog), IAppOllamaModel
{
    private const long Gibibyte = 1024L * 1024 * 1024;

    /// <inheritdoc />
    public override ModelDevice Device => onCard ? ModelDevice.Gpu : ModelDevice.Cpu;

    /// <inheritdoc />
    public long WeightBytes => info.Bytes;

    /// <inheritdoc />
    public long ContextTokens => Catalog.ContextTokens > 0 ? Catalog.ContextTokens : info.ContextTokens;

    /// <inheritdoc />
    /// <remarks>On the card: its card need, and about 1 GiB of main memory beside; on the processor: everything in main memory.</remarks>
    public override IReadOnlyList<ModelMemoryNeed> Needs
    {
        get
        {
            var card = AppModelAdvisor.CardBytes(info, ContextTokens);
            return [new ModelMemoryNeed(ModelQualities.Standard, onCard ? card : 0, onCard ? Gibibyte : card)];
        }
    }

    /// <inheritdoc />
    protected override async Task OnLoadAsync(CancellationToken cancellationToken) =>
        await server.StartAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    protected override async Task OnUnloadAsync(CancellationToken cancellationToken)
    {
        if (server.Address is not { } root)
        {
            // The server does not run: nothing of the model can be in memory.
            return;
        }

        if (await api.UnloadAsync(root, Id, cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation("{Model} was unloaded from {Server}", Id, AppOllamaDefaults.Name);
            return;
        }

        logger.LogWarning("{Server} still holds {Model} after the unload request; the server is ended so the graphics card is free", AppOllamaDefaults.Name, Id);
        await server.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
