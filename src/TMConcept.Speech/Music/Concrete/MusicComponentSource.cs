using TMConcept.Core.Components;

namespace TMConcept.Speech.Music;

/// <summary>The local music model's two components (0.1.46, C5): its environment and its weights.</summary>
public sealed class MusicComponentSource(RakkaseInstaller installer, RakkaseLayout layout) : IComponentSource
{
    private readonly IReadOnlyList<IComponent> _components = [new MusicEnvironmentComponent(installer, layout), new MusicWeightsComponent(installer, layout)];

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => _components;
}
