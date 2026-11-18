namespace TMConcept.Desktop.Updates;

/// <summary>
/// How this copy of the app came to the computer (0.1.26, the developer): both editions have the same features and use the same data folder,
/// so models and settings installed by one are seen by the other.
/// </summary>
public enum AppEditionKind
{
    /// <summary>The single program file (<c>TMConcept-Portable-win-x64.exe</c>, the Linux archive): nothing installed; it replaces itself on update.</summary>
    Portable = 0,

    /// <summary>Installed by Setup (Velopack, per user, no admin): Start menu and desktop shortcuts, removed from Windows' Apps list, updates with small deltas.</summary>
    Installed = 1,
}
