namespace TMConcept.Core.Models;

/// <summary>
/// Frees the models that something outside this run of the app keeps in graphics memory: what a local server (Ollama, LM Studio) still holds
/// from an earlier run, from before the app started or from another program. <see cref="IGpuCoordinator"/> runs every sweep once, before it
/// grants the first graphics card lease of a run (it knows nothing of those models itself).
/// </summary>
public interface IResidentModelSweep
{
    /// <summary>
    /// Unloads what is resident, except <paramref name="keep"/> (the model about to be used: no reload for nothing). Never throws but for
    /// cancellation.
    /// </summary>
    Task SweepAsync(IModel keep, CancellationToken cancellationToken);
}
