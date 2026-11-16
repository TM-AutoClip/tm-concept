using TMConcept.Core.Models;

namespace TMConcept.Speech.Python;

/// <summary>
/// A Python voice as a local model: on the NVIDIA card (CUDA) when there is one, otherwise on the processor (<see cref="PythonSpeechEngine.Device"/>).
/// Its needs come from the catalog (graphics card class and main memory). Unloading ends its Python worker, which frees the card's memory
/// and the main memory at once; the worker loads the model again on the next request.
/// </summary>
public sealed class PythonLocalModel(CatalogModel catalog, PythonSpeechEngine engine) : LocalModelBase(catalog)
{
    /// <inheritdoc />
    public override ModelDevice Device => engine.Device;

    /// <inheritdoc />
    protected override Task OnUnloadAsync(CancellationToken cancellationToken) => engine.UnloadAsync(cancellationToken);
}
