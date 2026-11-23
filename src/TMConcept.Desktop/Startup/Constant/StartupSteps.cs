namespace TMConcept.Desktop.Startup;

/// <summary>Names of the start steps that begin in one place and end in another (the step timer's log lines, see Core's IStepTimer).</summary>
public static class StartupSteps
{
    /// <summary>Avalonia's start: from <c>StartWithClassicDesktopLifetime</c> until the framework is initialized (App).</summary>
    public const string Avalonia = "startup.avalonia";
}
