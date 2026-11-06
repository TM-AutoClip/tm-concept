using TMConcept.Core.Components;
using TMConcept.Core.Tools;
using TMConcept.Speech.Embeddings;

namespace TMConcept.Speech.Components;

/// <summary>The local embedding model of the semantic duplicate check (after the sherpa-onnx libraries, whose ONNX Runtime it uses).</summary>
public sealed class EmbeddingComponent(LocalEmbedder embedder) : IComponent
{
    /// <inheritdoc />
    public string Code => ComponentCodes.EmbeddingPrefix + LocalEmbeddingModels.Id;

    /// <inheritdoc />
    public string Name => LocalEmbeddingModels.Name;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [ComponentCodes.SpeechRuntime];

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
