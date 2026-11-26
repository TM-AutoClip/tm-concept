using TMConcept.Core.Components;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Music;

/// <summary>
/// The composed music's SoundFont as a component (0.1.55; code <see cref="ComponentCodes.MusicSoundFont"/>; rule 18: never downloaded by itself):
/// MuseScore General 0.2 (MIT), about 40 MB, pinned by its SHA-256 (<see cref="SoundFontPins"/>).
/// </summary>
public sealed class SoundFontComponent(ISoundFontStore store) : IComponent
{
    /// <inheritdoc />
    public string Code => ComponentCodes.MusicSoundFont;

    /// <inheritdoc />
    public string Name => "MuseScore General SoundFont " + SoundFontPins.Version + " (MIT; instruments of the composed music)";

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public bool Supported => true;

    /// <inheritdoc />
    public bool Ready => store.Ready;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() => Ready ? [] : [new ComponentDownload(Name, SoundFontPins.Host, SoundFontPins.Bytes)];

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [store.Folder];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => store.InstallAsync(progress, cancellationToken);
}
