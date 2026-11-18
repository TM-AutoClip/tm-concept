using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// Deletes a folder or file for good (every delete of the app goes this way since 0.1.28, after the user confirmed it). Read-only files are
/// made writable first; a file a player or a voice worker lets go of a moment later is tried again (three tries, about 300 ms); then the
/// failure goes up with its reason.
/// </summary>
public sealed class PermanentDiscard(IAppPaths paths, ILogger<PermanentDiscard> logger) : FolderDiscardBase(paths, logger)
{
    private const int Tries = 3;

    /// <inheritdoc />
    protected override Task TakeAsync(string path, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Delete(path);
                return;
            }
            catch (IOException) when (attempt < Tries)
            {
                await Task.Delay(100 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }, cancellationToken);

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return;
        }

        if (!Directory.Exists(path))
        {
            return;
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(path, "*", options))
        {
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
        }

        Directory.Delete(path, recursive: true);
    }
}
