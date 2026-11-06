namespace TMConcept.Engines.Components;

/// <summary>
/// About how much an engine install downloads (shown in the confirmation; npm gives no size before the install). Measured roughly on the
/// pinned lock files; the browser is Chrome Headless Shell for one system.
/// </summary>
public static class EngineDownloads
{
    /// <summary>The npm registry.</summary>
    public const string NpmHost = "registry.npmjs.org";

    /// <summary>Where Chrome Headless Shell comes from (Chrome for Testing).</summary>
    public const string BrowserHost = "storage.googleapis.com";

    /// <summary>HyperFrames packages.</summary>
    public const long HyperFramesBytes = 90_000_000;

    /// <summary>Remotion packages.</summary>
    public const long RemotionBytes = 230_000_000;

    /// <summary>Revideo packages.</summary>
    public const long RevideoBytes = 160_000_000;

    /// <summary>Chrome Headless Shell.</summary>
    public const long BrowserBytes = 110_000_000;
}
