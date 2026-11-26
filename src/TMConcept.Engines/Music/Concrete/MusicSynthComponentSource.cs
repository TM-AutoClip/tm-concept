using TMConcept.Core.Components;

namespace TMConcept.Engines.Music;

/// <summary>The composed music's two components (0.1.55, "Program composes"): its synthesizer and its SoundFont.</summary>
public sealed class MusicSynthComponentSource(SpessaSynthRenderer renderer, ISoundFontStore soundFonts) : IComponentSource
{
    private readonly IReadOnlyList<IComponent> _components = [new MusicSynthComponent(renderer), new SoundFontComponent(soundFonts)];

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => _components;
}
