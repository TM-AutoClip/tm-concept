namespace TMConcept.Desktop.Startup;

/// <summary>
/// What shows the start's progress (the splash window): <see cref="IProgress{T}.Report"/> names the stage now running and may be called from
/// any thread; <see cref="ShowError"/> is called on the interface thread when a step failed.
/// </summary>
public interface IStartupScreen : IProgress<StartupStage>
{
    /// <summary>The start stopped at <paramref name="stage"/>: the reason is shown and the Cancel button becomes Close.</summary>
    void ShowError(StartupStage stage, string message);
}
