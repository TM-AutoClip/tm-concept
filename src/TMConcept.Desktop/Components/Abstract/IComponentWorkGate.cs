namespace TMConcept.Desktop.Components;

/// <summary>
/// Whether this process installs components right now (0.1.37, for the start-up sweep and the storage page): a component install and a
/// "Reinstall from scratch" hold it while they run (<see cref="Install"/>). What a run cut short leaves is known from the install journal
/// (<see cref="Core.Components.IInstallJournal"/>, 28 Nov 2026), not from a marker next to the components folder.
/// </summary>
public interface IComponentWorkGate
{
    /// <summary>Whether an install or a reinstall of this process runs.</summary>
    bool Busy { get; }

    /// <summary>Holds the gate for a component install or reinstall until disposed.</summary>
    IDisposable Install();
}
