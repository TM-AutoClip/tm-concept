using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;

namespace TMConcept.Core.Endpoints;

/// <summary>
/// A model of a user-added server on this computer (Ollama, LM Studio, llama-server, Lemonade…): taken to run on the graphics card. The server
/// loads it on its first request; unloading asks the registered <see cref="IEndpointModelControl"/>s with the server as it is saved now (or,
/// when it was deleted meanwhile, as it was when the model was first used). An unload that is not confirmed throws
/// <see cref="ModelUnloadException"/>: the server refused or still holds the model, or it is a server no control can ask (its own idle time
/// frees the card), so <see cref="IGpuCoordinator"/> knows the model may still hold the card.
/// </summary>
public sealed class EndpointLocalModel(CatalogModel catalog, LocalEndpoint endpoint, EndpointRegistry registry, IReadOnlyList<IEndpointModelControl> controls,
    ILogger logger)
    : LocalModelBase(catalog)
{
    /// <inheritdoc />
    public override ModelDevice Device => ModelDevice.Gpu;

    /// <inheritdoc />
    protected override async Task OnUnloadAsync(CancellationToken cancellationToken)
    {
        var server = EndpointCodes.TryId(Provider, out var id) && registry.Find(id) is { } saved ? saved : endpoint;
        foreach (var control in controls)
        {
            switch (await control.UnloadAsync(server, Id, cancellationToken).ConfigureAwait(false))
            {
                case EndpointUnload.Unloaded:
                    logger.LogInformation("{Model} was unloaded from its local server", Key);
                    return;
                case EndpointUnload.Failed:
                    throw new ModelUnloadException($"The local server still holds {Id}.");
            }
        }

        throw new ModelUnloadException($"The local server of {Id} cannot be asked to unload it; it frees the graphics card after its own idle time.");
    }
}
