using TMConcept.Core.Components;
using TMConcept.Core.Engines;
using TMConcept.Core.Tools;
using TMConcept.Engines.Engines;

namespace TMConcept.Engines.Components;

/// <summary>
/// A video engine: its npm packages and its browser (Chrome Headless Shell), after Node.js and ffmpeg. Ready only when the whole engine can
/// render, so a missing Node.js or ffmpeg pulls those in; the install itself fetches only the packages and the browser that are missing.
/// </summary>
public sealed class EngineComponent(IVideoEngine engine) : IComponent
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
    public bool Supported => true;

    /// <inheritdoc />
    public bool Ready => Engine.Status().Ready;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads()
    {
        var missing = Engine.Status().Missing;
        var list = new List<ComponentDownload>();
        if (missing.Contains("packages"))
        {
            list.Add(new ComponentDownload(Engine.Name + " (npm)", EngineDownloads.NpmHost, Engine.Kind switch
            {
                EngineKind.Remotion => EngineDownloads.RemotionBytes,
                EngineKind.Revideo => EngineDownloads.RevideoBytes,
                _ => EngineDownloads.HyperFramesBytes,
            }));
        }

        if (missing.Contains("browser"))
        {
            list.Add(new ComponentDownload("Chrome Headless Shell", EngineDownloads.BrowserHost, EngineDownloads.BrowserBytes));
        }

        return list;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => Engine is VideoEngineBase known ? [known.PackageRoot] : [];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var missing = Engine.Status().Missing;
        return missing.Contains("packages") || missing.Contains("browser") ? Engine.InstallAsync(agreed: true, progress, cancellationToken) : Task.CompletedTask;
    }
}
