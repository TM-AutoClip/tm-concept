using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Os;

/// <summary>
/// Opens a folder, a file or a web address with the operating system's default program (Explorer / the file manager through xdg-open, the
/// default browser), and shows a file in the file manager the operating system module's way. Only folders and files inside the app's own
/// folders are opened by the pages.
/// </summary>
public sealed class ShellLauncher(IOsModule os, ILogger<ShellLauncher> logger) : IShellLauncher
{
    /// <inheritdoc />
    public void Open(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true }, address.Host);
    }

    /// <inheritdoc />
    public void OpenPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Directory.CreateDirectory(Directory.Exists(path) || !Path.HasExtension(path) ? path : Path.GetDirectoryName(path)!);
        Start(new ProcessStartInfo(path) { UseShellExecute = true }, Path.GetFileName(path));
    }

    /// <inheritdoc />
    public void Reveal(string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        Start(os.RevealCommand(Path.GetFullPath(file)), Path.GetFileName(file));
    }

    private void Start(ProcessStartInfo start, string label)
    {
        try
        {
            using var _ = Process.Start(start);
            logger.LogInformation("Opened {Label}", label);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogError(ex, "Could not open {Label}", label);
            throw;
        }
    }
}
