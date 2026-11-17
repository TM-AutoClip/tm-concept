using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;

namespace TMConcept.Core.AppOllama;

/// <summary>
/// Before the first graphics card lease of a run: when the app's own Ollama server already runs (a pull started it, a page used it), every
/// model it holds is unloaded except the one about to be used. A server that does not run holds nothing. Never throws but for cancellation.
/// </summary>
public sealed class AppOllamaResidentSweep(IAppOllamaServer server, IAppOllamaApi api, ILogger<AppOllamaResidentSweep> logger) : IResidentModelSweep
{
    /// <inheritdoc />
    public async Task SweepAsync(IModel keep, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keep);
        if (server.Address is not { } root)
        {
            return;
        }

        try
        {
            foreach (var tag in await api.LoadedAsync(root, cancellationToken).ConfigureAwait(false))
            {
                if (keep.Provider == ModelProviders.AppOllama && Same(keep.Id, tag))
                {
                    continue;
                }

                if (await api.UnloadAsync(root, tag, cancellationToken).ConfigureAwait(false))
                {
                    logger.LogInformation("{Model} was still loaded in {Server}; unloaded so the graphics card is free", tag, AppOllamaDefaults.Name);
                }
                else
                {
                    logger.LogWarning("{Model} is still loaded in {Server} and could not be unloaded; it may hold graphics memory", tag, AppOllamaDefaults.Name);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            logger.LogWarning(ex, "{Server} could not be asked which models it holds", AppOllamaDefaults.Name);
        }
    }

    /// <summary>Whether two Ollama names are the same model (<c>gemma4</c> is <c>gemma4:latest</c>).</summary>
    public static bool Same(string a, string b)
    {
        static string Full(string name) => name.Contains(':', StringComparison.Ordinal) ? name : name + ":latest";
        return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);
    }
}
