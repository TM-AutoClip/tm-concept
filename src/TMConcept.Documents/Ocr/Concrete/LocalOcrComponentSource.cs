using TMConcept.Core.Components;

namespace TMConcept.Documents.Ocr;

/// <summary>The local OCR as a component source.</summary>
public sealed class LocalOcrComponentSource(LocalOcrEngine engine) : IComponentSource
{
    private readonly IComponent _component = new LocalOcrComponent(engine);

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => [_component];
}
