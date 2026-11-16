using TMConcept.Core.Models;
using TMConcept.Core.Speech;

namespace TMConcept.Speech.Python;

/// <summary>The Python voices as <see cref="ILocalModel"/>s (registered before the speech worker's source, which takes every other local voice).</summary>
public sealed class PythonModelSource(IEnumerable<ISpeechEngine> engines) : ILocalModelSource
{
    private readonly Lazy<IReadOnlyList<PythonSpeechEngine>> _engines = new(() => engines.OfType<PythonSpeechEngine>().ToList());

    /// <inheritdoc />
    public ILocalModel? Create(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider == ModelProviders.Local && model.Capabilities.HasFlag(ModelCapability.Speech)
            && _engines.Value.FirstOrDefault(e => e.Code == model.Id) is { } engine
            ? new PythonLocalModel(model, engine)
            : null;
    }
}
