using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;

namespace TMConcept.Core.Endpoints;

/// <summary>
/// Before the first graphics card lease of a run: every saved Ollama and LM Studio server on this computer is asked which models it holds
/// (an earlier run left them, LM Studio keeps an explicitly loaded model with no time limit, Ollama for its keep-alive) and each one is
/// unloaded through the controls, except the model about to be used. A server that does not answer holds nothing to free.
/// </summary>
public sealed class EndpointResidentSweep(EndpointRegistry registry, IEndpointProbe probe, IEnumerable<IEndpointModelControl> controls, ILogger<EndpointResidentSweep> logger)
    : IResidentModelSweep
{
    private readonly IReadOnlyList<IEndpointModelControl> _controls = controls.ToList();

    /// <inheritdoc />
    public async Task SweepAsync(IModel keep, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keep);
        foreach (var endpoint in registry.All.Where(e => e.Kind != EndpointKind.Generic && EndpointModelSource.OnThisComputer(e.BaseUrl)))
        {
            IReadOnlyList<LoadedServerModel> loaded;
            try
            {
                loaded = await probe.LoadedAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                logger.LogInformation("A local server could not be asked which models it holds ({Reason})", ex.Message);
                continue;
            }

            var provider = EndpointCodes.For(endpoint.Id);
            foreach (var model in loaded.Where(m => m.Id.Length > 0 && !(keep.Provider == provider && keep.Id == m.Id)))
            {
                var result = EndpointUnload.NotMine;
                foreach (var control in _controls)
                {
                    result = await control.UnloadAsync(endpoint, model.Id, cancellationToken).ConfigureAwait(false);
                    if (result != EndpointUnload.NotMine)
                    {
                        break;
                    }
                }

                if (result == EndpointUnload.Unloaded)
                {
                    logger.LogInformation("{Model} was still loaded in a local server from before; unloaded so the graphics card is free", model.Id);
                }
                else
                {
                    logger.LogWarning("{Model} is still loaded in a local server from before and could not be unloaded; it may hold graphics memory", model.Id);
                }
            }
        }
    }
}
