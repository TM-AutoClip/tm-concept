using TMConcept.Core.Components;
using TMConcept.Core.Tools;

namespace TMConcept.Core.Music;

/// <summary>
/// The ready music library as a component (0.1.45, C4; rule 18: nothing is downloaded without the user's yes): every track of the catalog
/// from incompetech.com, listed with its size in the one confirmation of the install flow, never part of the app's file.
/// </summary>
public sealed class MusicLibraryComponent(MusicLibraryStore store) : IComponent
{
    /// <inheritdoc />
    public string Code => ComponentCodes.MusicLibrary;

    /// <inheritdoc />
    public string Name => "Music library (Kevin MacLeod, incompetech.com, CC BY 4.0)";

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public bool Supported => true;

    /// <inheritdoc />
    public bool Ready => store.Ready;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() => store.Ready
        ? []
        : store.Missing().Select(t => new ComponentDownload("Music · " + t.Title, MusicLibraryRules.Host, t.Bytes)).ToList();

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [store.Folder];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => store.InstallAsync(progress, cancellationToken);
}
