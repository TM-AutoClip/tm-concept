using TMConcept.Core.Models;

namespace TMConcept.Speech.Speech;

/// <summary>
/// A local voice or the local embedder: both run in the speech worker (sherpa-onnx and ONNX Runtime on the processor), so they never take the
/// graphics card. Unloading stops the worker when it is idle (it stops by itself after a minute without work anyway).
/// </summary>
public sealed class WorkerLocalModel(CatalogModel catalog, SpeechWorkerClient worker) : LocalModelBase(catalog)
{
    /// <inheritdoc />
    public override ModelDevice Device => ModelDevice.Cpu;

    /// <inheritdoc />
    protected override Task OnUnloadAsync(CancellationToken cancellationToken)
    {
        worker.StopIfIdle();
        return Task.CompletedTask;
    }
}