using TMConcept.Core.Components;
using TMConcept.Core.Documents;
using TMConcept.Core.Tools;

namespace TMConcept.Documents.Ocr;

/// <summary>
/// The DirectML runtime of the local OCR as a component (0.1.27, Windows x64 with a graphics card): offered with the local OCR when the
/// "Text recognition" setting is Automatic, downloaded only after the user confirmed it (its confirmation names Microsoft's licence).
/// </summary>
public sealed class DirectMlRuntimeComponent(DirectMlRuntimeStore store) : IComponent
{
    /// <inheritdoc />
    public string Code => PageTextEngines.OcrGpuComponent;

    /// <inheritdoc />
    public string Name => DirectMlRuntimeRelease.Name;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public bool Supported => store.Supported;

    /// <inheritdoc />
    public bool Ready => store.Ready;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() =>
        store.Missing.Select(f => new ComponentDownload(Name + " · " + f.FileName, f.Url.Host, f.CompressedBytes)).ToList();

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [store.Folder];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => store.InstallAsync(progress, cancellationToken);
}
