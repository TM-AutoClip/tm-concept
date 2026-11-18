namespace TMConcept.Desktop.Updates;

/// <summary>Starts the downloaded, checked installer (tests record the call instead; no test ever starts one).</summary>
public interface ISetupLauncher
{
    /// <summary>Starts <paramref name="setup"/> as its own process (no shell, no arguments: Setup installs to its own folder).</summary>
    void Start(string setup);
}
