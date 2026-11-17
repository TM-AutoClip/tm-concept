using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Desktop.Os;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// Moves a folder to the recycle bin / trash (<see cref="IRecycleBin"/>). When the bin cannot take it (turned off, too small, no trash on
/// this system, a declined Windows question) it throws, and the storage manager asks before it deletes anything for good.
/// </summary>
public sealed class RecycleBinDiscard(IRecycleBin bin, IAppPaths paths, ILogger<RecycleBinDiscard> logger) : FolderDiscardBase(paths, logger)
{
    /// <inheritdoc />
    public override bool Permanent => false;

    /// <inheritdoc />
    protected override Task TakeAsync(string path, CancellationToken cancellationToken) => bin.MoveAsync(path, cancellationToken);
}
