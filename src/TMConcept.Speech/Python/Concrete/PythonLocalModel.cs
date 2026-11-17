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

    /// <summary>
    /// PyTorch keeps every weight of the voice on the card (it cannot leave some in main memory): with less than three quarters of its class
    /// free (the class figure has headroom) it would end out of memory, so the memory check asks first.
    /// </summary>
    protected override long MinimumCardBytes(ModelMemoryNeed need)
    {
        ArgumentNullException.ThrowIfNull(need);
        return need.VideoMemoryBytes / 4 * 3;
    }

    /// <summary>PyTorch loads every weight on the card: from the minimum up it runs there, below it is refused; never "main memory".</summary>
    protected override bool OffloadsToMainMemory => false;

    /// <inheritdoc />
    protected override Task OnUnloadAsync(CancellationToken cancellationToken) => engine.UnloadAsync(cancellationToken);
}
