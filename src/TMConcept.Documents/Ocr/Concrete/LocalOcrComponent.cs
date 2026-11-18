using TMConcept.Core.Components;
using TMConcept.Core.Tools;

namespace TMConcept.Documents.Ocr;

/// <summary>The local OCR's two models as a component (downloaded only after the user confirmed the download list with its sizes).</summary>
public sealed class LocalOcrComponent(LocalOcrEngine engine) : IComponent
{
    /// <inheritdoc />
    public string Code => LocalOcrModels.ComponentCode;

    /// <inheritdoc />
    public string Name => LocalOcrModels.Name;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public bool Supported => true;

    /// <inheritdoc />
    public bool Ready => engine.Ready;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() =>
        engine.Missing.Select(f => new ComponentDownload(Name + " · " + f.ExecutablePath, f.Url.Host, f.ApproximateBytes)).ToList();

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [engine.Folder];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => engine.InstallAsync(progress, cancellationToken);
}
