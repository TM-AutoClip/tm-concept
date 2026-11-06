using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;
using TMConcept.Storage.Keys;

namespace TMConcept.Desktop.Os;

/// <summary>
/// Linux trash: <c>gio trash</c> when it exists, otherwise the XDG trash folder (files + .trashinfo) in the home folder. A failed
/// <c>gio trash</c> is a warning with its exit code and error before the XDG trash folder is used.
/// </summary>
public sealed class LinuxRecycleBin(ICommandRunner runner, ILogger<LinuxRecycleBin> logger) : IRecycleBin
{
    /// <inheritdoc />
    public async Task MoveAsync(string folder, CancellationToken cancellationToken)
    {
        if (runner.Exists("gio"))
        {
            var (code, _, error) = await runner.RunAsync("gio", ["trash", folder], null, cancellationToken).ConfigureAwait(false);
            if (code == 0)
            {
                return;
            }

            if (!Directory.Exists(folder))
            {
                throw new IOException("gio trash failed: " + ChildOutput.Summary(error));
            }

            logger.LogWarning("gio trash exited {Code}: {Error}; using the XDG trash folder", code, ChildOutput.Summary(error));
        }
        else
        {
            logger.LogInformation("gio is not installed; using the XDG trash folder");
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var trash = Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share") : xdg, "Trash");
        Directory.CreateDirectory(Path.Combine(trash, "files"));
        Directory.CreateDirectory(Path.Combine(trash, "info"));
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        var target = name;
        for (var i = 2; Directory.Exists(Path.Combine(trash, "files", target)); i++)
        {
            target = name + "." + i.ToString(CultureInfo.InvariantCulture);
        }

        await File.WriteAllTextAsync(Path.Combine(trash, "info", target + ".trashinfo"),
            "[Trash Info]\nPath=" + Uri.EscapeDataString(folder).Replace("%2F", "/", StringComparison.Ordinal) + "\nDeletionDate="
            + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "\n", cancellationToken).ConfigureAwait(false);
        Directory.Move(folder, Path.Combine(trash, "files", target));
    }
}
