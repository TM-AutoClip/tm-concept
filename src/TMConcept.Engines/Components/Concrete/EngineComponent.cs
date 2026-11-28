using TMConcept.Core.Components;
using TMConcept.Core.Engines;
using TMConcept.Core.Tools;
using TMConcept.Engines.Engines;

namespace TMConcept.Engines.Components;

/// <summary>
/// A video engine: its npm packages and its browser (Chrome Headless Shell), after Node.js and ffmpeg. Ready only when the whole engine can
/// render, so a missing Node.js or ffmpeg pulls those in; the install itself fetches only the packages and the browser that are missing.
/// </summary>
public sealed class EngineComponent(IVideoEngine engine) : IComponent, IComponentUses
{
    /// <summary>The engine.</summary>
    public IVideoEngine Engine { get; } = engine;

    /// <summary>The component code of an engine kind.</summary>
    public static string CodeOf(EngineKind kind) => ComponentCodes.EnginePrefix + kind.ToString().ToLowerInvariant();

    /// <inheritdoc />
    public string Code => CodeOf(Engine.Kind);

    /// <inheritdoc />
    public string Name => Engine.Name;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [ComponentCodes.Node, ComponentCodes.Ffmpeg];

    /// <inheritdoc />
    /// <remarks>
    /// HyperFrames and Revideo render with the shared browser (<c>tools/chrome</c>); Remotion only with its own copy inside its packages. The
    /// engine's install fetches it, so it is not a <see cref="DependsOn"/>.
    /// </remarks>
    public IReadOnlyList<string> Uses => Engine.Kind == EngineKind.Remotion ? [] : [ComponentCodes.Browser];

    /// <inheritdoc />
    public bool Supported => true;

    /// <inheritdoc />
    public bool Ready => Engine.Status().Ready;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads()
    {
        var missing = Engine.Status().Missing;
        var list = new List<ComponentDownload>();
        if (missing.Contains(EngineNeeds.Packages))
        {
            list.Add(new ComponentDownload(Engine.Name + " (npm)", EngineDownloads.NpmHost, Engine.Kind switch
            {
                EngineKind.Remotion => EngineDownloads.RemotionBytes,
                EngineKind.Revideo => EngineDownloads.RevideoBytes,
                _ => EngineDownloads.HyperFramesBytes,
            }));
        }

        if (missing.Contains(EngineNeeds.Browser))
        {
            list.Add(new ComponentDownload("Chrome Headless Shell", EngineDownloads.BrowserHost, EngineDownloads.BrowserBytes));
        }

        return list;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => Engine is VideoEngineBase known ? [known.PackageRoot] : [];

    /// <inheritdoc />
    /// <remarks>
    /// Its packages folder only when the packages are what is missing: an engine that waits only for Node.js, ffmpeg or the shared browser
    /// keeps its packages (its install downloads none of them again).
    /// </remarks>
    public IReadOnlyList<string> ReplacedPaths() => Engine.Status().Missing.Contains(EngineNeeds.Packages) ? Folders() : [];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var missing = Engine.Status().Missing;
        return missing.Contains(EngineNeeds.Packages) || missing.Contains(EngineNeeds.Browser)
            ? Engine.InstallAsync(agreed: true, progress, cancellationToken)
            : Task.CompletedTask;
    }
}
