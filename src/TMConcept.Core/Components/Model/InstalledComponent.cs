namespace TMConcept.Core.Components;

/// <summary>
/// One entry of <see cref="IInstallRecord"/>: a component's code, the app version it was installed with (empty = found on disk from before
/// the record existed) and when it was recorded.
/// </summary>
public sealed class InstalledComponent
{
    /// <summary>Component code (<see cref="ComponentCodes"/>).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The app version that installed it; empty when it was found on disk from before the record existed.</summary>
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>When it was recorded (UTC).</summary>
    public DateTimeOffset InstalledAtUtc { get; set; }
}
