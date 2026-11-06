using TMConcept.Core.Components;
using TMConcept.Speech.Embeddings;

namespace TMConcept.Speech.Components;

/// <summary>The local embedding model as a component.</summary>
public sealed class EmbeddingComponentSource(LocalEmbedder embedder) : IComponentSource
{
    private readonly IComponent _component = new EmbeddingComponent(embedder);

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => [_component];
}
