using TMConcept.Core.Components;
using TMConcept.Core.Tools;
using TMConcept.Engines.Images;

namespace TMConcept.Engines.Components;

/// <summary>A local image model's weights (after stable-diffusion.cpp).</summary>
public sealed class ImageModelComponent(LocalImageModel model, LocalImageStore store, IToolLocator tools) : IComponent
{
    /// <inheritdoc />
    public string Code => ComponentCodes.ImagePrefix + model.Id;

    /// <inheritdoc />
    public string Name => model.Name;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [ComponentCodes.ImageRuntime];

    /// <inheritdoc />
    public bool Supported => true;

    /// <inheritdoc />
    public bool Ready => store.Missing(model).Count == 0 && tools.Status(ToolKind.ImageRuntime).Ready;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() =>
        store.Missing(model).Select(f => new ComponentDownload(model.Name + " · " + f.ExecutablePath, f.Url.Host, f.ApproximateBytes)).ToList();

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [store.Folder(model)];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => store.InstallAsync(model, progress, cancellationToken);
}
