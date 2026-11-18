using TMConcept.Core.Components;
using TMConcept.Core.Documents;
using TMConcept.Core.Tools;

namespace TMConcept.Documents.Pdf;

/// <summary>PDFium as a component (0.1.27): installed with the engine of "Text from scanned pages", after the user confirmed the download list.</summary>
public sealed class PdfiumComponent(PdfiumStore store) : IComponent
{
    /// <inheritdoc />
    public string Code => PageTextEngines.PdfRendererComponent;

    /// <inheritdoc />
    public string Name => PdfiumRelease.Name;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public bool Supported => store.Supported;

    /// <inheritdoc />
    public bool Ready => store.Ready;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() => store.Ready || store.Archive is not { } archive
        ? []
        : [new ComponentDownload(Name, archive.Url.Host, archive.ApproximateBytes)];

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [store.Folder];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => store.InstallAsync(progress, cancellationToken);
}
