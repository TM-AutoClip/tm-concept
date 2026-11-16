using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;

namespace TMConcept.Core.Endpoints;

/// <summary>
/// A model of a user-added server on this computer (Ollama, LM Studio, llama-server, Lemonade…): taken to run on the graphics card. The server
/// loads it on its first request; unloading asks the registered <see cref="IEndpointModelControl"/>s, and a server none of them knows keeps
/// it (said once in the log: its own idle time frees the card).
/// </summary>
public sealed class EndpointLocalModel(CatalogModel catalog, EndpointRegistry registry, IReadOnlyList<IEndpointModelControl> controls, ILogger logger)
    : LocalModelBase(catalog)
{
    private bool _told;

    /// <inheritdoc />
    public override ModelDevice Device => ModelDevice.Gpu;

    /// <inheritdoc />
    protected override async Task OnUnloadAsync(CancellationToken cancellationToken)
    {
        if (!EndpointCodes.TryId(Provider, out var id) || registry.Find(id) is not { } endpoint)
        {
            return;
        }

        foreach (var control in controls)
        {
            if (await control.UnloadAsync(endpoint, Id, cancellationToken).ConfigureAwait(false))
            {
                logger.LogInformation("{Model} was unloaded from its local server", Key);
                return;
            }
        }

        if (!_told)
        {
            _told = true;
            logger.LogInformation("The local server of {Model} cannot be asked to unload it; it frees the graphics card after its own idle time", Key);
        }
    }
}