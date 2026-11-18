using TMConcept.Core.Components;

namespace TMConcept.Documents.Ocr;

/// <summary>The DirectML runtime of the local OCR as a component source (0.1.27).</summary>
public sealed class DirectMlRuntimeComponentSource(DirectMlRuntimeStore store) : IComponentSource
{
    private readonly IComponent _component = new DirectMlRuntimeComponent(store);

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => [_component];
}
