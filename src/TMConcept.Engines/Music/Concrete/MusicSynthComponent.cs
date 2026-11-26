using TMConcept.Core.Components;
using TMConcept.Core.Tools;
using TMConcept.Engines.Components;

namespace TMConcept.Engines.Music;

/// <summary>
/// The composed music's synthesizer as a component (0.1.55; code <see cref="ComponentCodes.MusicSynth"/>, after Node.js): the pinned npm packages
/// of spessasynth_core, about 1.4 MB from registry.npmjs.org.
/// </summary>
public sealed class MusicSynthComponent(SpessaSynthRenderer renderer) : IComponent
{
    /// <inheritdoc />
    public string Code => ComponentCodes.MusicSynth;

    /// <inheritdoc />
    public string Name => "Music synthesizer for the composed music (spessasynth_core, Apache-2.0)";

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [ComponentCodes.Node];

    /// <inheritdoc />
    public bool Supported => true;

    /// <inheritdoc />
    public bool Ready => renderer.PackagesReady;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() => Ready ? [] : [new ComponentDownload(Name + " (npm)", EngineDownloads.NpmHost, MusicSynthRules.PackageBytes)];

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [renderer.Package.Root];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => renderer.Package.InstallAsync(progress, cancellationToken);
}
