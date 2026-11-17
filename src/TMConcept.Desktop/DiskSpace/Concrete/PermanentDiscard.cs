using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>Deletes a folder or file for good. Used only after the user confirmed it, when the recycle bin could not take it.</summary>
public sealed class PermanentDiscard(IAppPaths paths, ILogger<PermanentDiscard> logger) : FolderDiscardBase(paths, logger)
{
    /// <inheritdoc />
    public override bool Permanent => true;

    /// <inheritdoc />
    protected override Task TakeAsync(string path, CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            File.Delete(path);
        }
    }, cancellationToken);
}
