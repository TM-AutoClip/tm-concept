using TMConcept.Core.Components;
using TMConcept.Core.Models;
using TMConcept.Core.Speech;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Components;

/// <summary>
/// The components of the Python voices: the Python, one environment per <see cref="PythonEnvironment"/> a registered voice uses, and each
/// voice's model files (with the licence of its catalog entry). uv itself is a tool component of the engines' source.
/// </summary>
public sealed class PythonComponentSource(IEnumerable<ISpeechEngine> voices, IModelCatalog catalog, PythonInstaller installer, PythonLayout layout,
    PythonModelStore models) : IComponentSource
{
    private readonly Lazy<IReadOnlyList<IComponent>> _components = new(() =>
    {
        var engines = voices.OfType<PythonSpeechEngine>().ToList();
        if (engines.Count == 0)
        {
            return [];
        }

        return
        [
            new PythonRuntimeComponent(installer, layout),
            .. engines.Select(e => e.Environment).Distinct().Select(e => new PythonEnvironmentComponent(e, installer, layout)),
            .. engines.Select(e => catalog.Find(ModelProviders.Local, e.Code) is { } model
                ? new PythonVoiceComponent(e, models, model.License.ToUpperInvariant(), model.LicenseUse == LicenseUse.NonCommercial)
                : new PythonVoiceComponent(e, models, string.Empty, false)),
        ];
    });

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => _components.Value;
}
