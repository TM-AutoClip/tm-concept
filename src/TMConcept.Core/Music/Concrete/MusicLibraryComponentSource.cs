using TMConcept.Core.Components;

namespace TMConcept.Core.Music;

/// <summary>The ready music library as a component source (0.1.45).</summary>
public sealed class MusicLibraryComponentSource(MusicLibraryStore store) : IComponentSource
{
    private readonly IComponent _component = new MusicLibraryComponent(store);

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => [_component];
}
