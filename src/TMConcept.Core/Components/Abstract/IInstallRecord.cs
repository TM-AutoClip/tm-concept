namespace TMConcept.Core.Components;

/// <summary>
/// The record of what this app installed, with the app version each component was installed with: kept outside the components folder, so
/// it outlives "Reinstall from scratch" (the folder is deleted first) and a components folder that was moved or emptied. The update after a
/// new app version installs again what this record (and the disk) says was installed, so a declined or failed reinstall asks again at the
/// next start instead of forgetting the components (0.1.16).
/// </summary>
public interface IInstallRecord
{
    /// <summary>Every recorded component, oldest first.</summary>
    IReadOnlyList<InstalledComponent> List();

    /// <summary>
    /// Records <paramref name="code"/> as installed with <paramref name="appVersion"/> (null = this app's version; empty = found on disk from
    /// before the record existed, which never replaces an entry that names its version).
    /// </summary>
    void Installed(string code, string? appVersion = null);

    /// <summary>Forgets <paramref name="code"/> (the user removed it).</summary>
    void Removed(string code);
}
