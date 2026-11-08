using TMConcept.Core.Components;
using TMConcept.Core.Speech;
using TMConcept.Speech.Speech;

namespace TMConcept.Speech.Components;

/// <summary>The local voices as components.</summary>
public sealed class VoiceComponentSource(IEnumerable<ISpeechEngine> voices) : IComponentSource
{
    private readonly Lazy<IReadOnlyList<IComponent>> _components =
        new(() => voices.OfType<LocalSpeechEngineBase>().Select(v => (IComponent)new VoiceComponent(v)).ToList());

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => _components.Value;
}
