using TMConcept.Core.Components;

namespace TMConcept.Documents.Pdf;

/// <summary>PDFium as a component source (0.1.27).</summary>
public sealed class PdfiumComponentSource(PdfiumStore store) : IComponentSource
{
    private readonly IComponent _component = new PdfiumComponent(store);

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => [_component];
}
