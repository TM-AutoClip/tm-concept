using TMConcept.Core.Components;
using TMConcept.Core.Speech;
using TMConcept.Speech.Speech;

namespace TMConcept.Speech.Components;

/// <summary>The local voices as components.</summary>
public sealed class VoiceComponentSource(IEnumerable<ISpeechEngine> voices, SpeechModelInstaller installer) : IComponentSource
{
    private readonly Lazy<IReadOnlyList<IComponent>> _components =
        new(() => voices.OfType<SherpaSpeechEngineBase>().Select(v => (IComponent)new VoiceComponent(v, installer)).ToList());

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => _components.Value;
}
