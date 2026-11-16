using TMConcept.Core.Models;

namespace TMConcept.Speech.Speech;

/// <summary>The local voices and the local embedder (both in the speech worker) as <see cref="ILocalModel"/>s.</summary>
public sealed class WorkerModelSource(SpeechWorkerClient worker) : ILocalModelSource
{
    /// <inheritdoc />
    public ILocalModel? Create(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider == ModelProviders.Local && (model.Capabilities.HasFlag(ModelCapability.Speech) || model.Capabilities.HasFlag(ModelCapability.Embedding))
            ? new WorkerLocalModel(model, worker)
            : null;
    }
}