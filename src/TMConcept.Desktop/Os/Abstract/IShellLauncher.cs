namespace TMConcept.Desktop.Os;

/// <summary>
/// Opens a web address, a folder or a file with the operating system's default program, or shows a file in the file manager. Pages that
/// open the browser take this (tests give their own and only check the address; no test opens a browser).
/// </summary>
public interface IShellLauncher
{
    /// <summary>Opens a web address in the default browser.</summary>
    void Open(Uri address);

    /// <summary>Opens a folder or a file.</summary>
    void OpenPath(string path);

    /// <summary>Shows <paramref name="file"/> in the file manager (Windows: selected in Explorer; Linux: its folder is opened).</summary>
    void Reveal(string file);
}
