using TMConcept.Core.Components;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// The source a local music family runs from as a component (28 Nov 2026; code <see cref="ComponentCodes.MusicCodePrefix"/> + the source's name:
/// "music-code.audiocraft"). A pinned GitHub source archive, unpacked; downloaded only after the user's yes, with the family's first model.
/// </summary>
public sealed class LocalMusicSourceComponent(LocalMusicFamily family, string name, LocalMusicStore store, LocalMusicLayout layout) : IComponent
{
    /// <inheritdoc />
    public string Code => ComponentCodes.MusicCodePrefix + name;

    /// <inheritdoc />
    public string Name => family.Name + " source (" + name + ", " + family.SourceLicense + ")";

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public bool Supported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <inheritdoc />
    public bool Ready => store.SourceReady(family);

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() => Ready || family.Source is not { } source
        ? []
        : [new ComponentDownload(Name + " " + source.Version[..7], source.Url.Host, source.ApproximateBytes)];

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [layout.SourceFolder(family)];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => store.InstallSourceAsync(family, progress, cancellationToken);
}
