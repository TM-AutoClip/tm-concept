namespace TMConcept.Desktop.Os;

/// <summary>Writes the installed edition's shortcuts again (Settings → General → "Recreate shortcuts"); Windows only.</summary>
public interface IShortcutWriter
{
    /// <summary>Whether this system has such shortcuts.</summary>
    bool Supported { get; }

    /// <summary>
    /// Writes <c>TM Concept.lnk</c> on the desktop and in the Start menu, starting <paramref name="program"/> with the taskbar id
    /// <paramref name="appUserModelId"/>; returns the files written. Throws <see cref="IOException"/> when one could not be written.
    /// </summary>
    IReadOnlyList<string> Write(string program, string appUserModelId);
}
