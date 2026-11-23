namespace TMConcept.Desktop.Components;

/// <summary>
/// Whether this process installs components right now (0.1.37, for the start-up sweep): a component install (<see cref="Install"/>) and a
/// "Reinstall from scratch" (<see cref="Reinstall"/>) hold it while they run. A reinstall also leaves a marker file next to <c>tools</c>
/// (<see cref="MarkerFile"/>) until it ends, so the next start knows when one was cut short (a crash, a power cut): one app runs per data
/// folder, so a marker this process did not write is always a previous run's.
/// </summary>
public interface IComponentWorkGate
{
    /// <summary>The marker file a running reinstall keeps next to <c>tools</c>.</summary>
    string MarkerFile { get; }

    /// <summary>Whether an install or a reinstall of this process runs.</summary>
    bool Busy { get; }

    /// <summary>Whether a reinstall of this process runs.</summary>
    bool Reinstalling { get; }

    /// <summary>Whether a previous run's reinstall was cut short: its marker is there and no reinstall of this process runs.</summary>
    bool Interrupted { get; }

    /// <summary>Holds the gate for a component install until disposed.</summary>
    IDisposable Install();

    /// <summary>Holds the gate for a reinstall and writes the marker file until disposed.</summary>
    IDisposable Reinstall();

    /// <summary>
    /// The marker stays when the running reinstall ends (its old folder could not be put back in place): the next start then treats it as a
    /// cut-short reinstall and keeps the old folders once more. Nothing when no reinstall of this process runs.
    /// </summary>
    void KeepMarker();

    /// <summary>Deletes a previous run's marker (<see cref="Interrupted"/>) so the next start sweeps again; nothing while a reinstall runs.</summary>
    void ForgetInterrupted();
}
