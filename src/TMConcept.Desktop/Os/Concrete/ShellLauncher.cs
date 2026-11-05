using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Os;

/// <summary>
/// Opens a folder, a file or a web address with the operating system's default program (Explorer / the file manager through xdg-open, the
/// default browser). Only folders and files inside the app's own folders are opened by the pages.
/// </summary>
public sealed class ShellLauncher(ILogger<ShellLauncher> logger)
{
    /// <summary>Opens a web address.</summary>
    public void Open(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Start(address.AbsoluteUri, address.Host);
    }

    /// <summary>Opens a folder or a file.</summary>
    public void OpenPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Directory.CreateDirectory(Directory.Exists(path) || !Path.HasExtension(path) ? path : Path.GetDirectoryName(path)!);
        Start(path, Path.GetFileName(path));
    }

    private void Start(string target, string label)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogError(ex, "Could not open {Label}", label);
            throw;
        }
    }
}
