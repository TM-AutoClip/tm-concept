using TMConcept.Core.Components;

namespace TMConcept.Documents.Embeddings;

/// <summary>The documents' meaning model as a component source.</summary>
public sealed class E5ComponentSource(E5Embedder embedder) : IComponentSource
{
    private readonly IComponent _component = new E5Component(embedder);

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => [_component];
}
