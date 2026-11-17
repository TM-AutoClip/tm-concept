using TMConcept.Core.Components;
using TMConcept.Core.Tools;

namespace TMConcept.Documents.Embeddings;

/// <summary>The documents' meaning model as a component (downloaded only after the user confirmed the download list).</summary>
public sealed class E5Component(E5Embedder embedder) : IComponent
{
    /// <inheritdoc />
    public string Code => embedder.ComponentCode;

    /// <inheritdoc />
    public string Name => E5Model.Name;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public bool Supported => true;

    /// <inheritdoc />
    public bool Ready => embedder.Ready;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() =>
        embedder.Missing.Select(f => new ComponentDownload(Name + " · " + f.ExecutablePath, f.Url.Host, f.ApproximateBytes)).ToList();

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [embedder.Folder];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => embedder.InstallAsync(progress, cancellationToken);
}
