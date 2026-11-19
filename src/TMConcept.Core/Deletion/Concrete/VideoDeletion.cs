using Microsoft.Extensions.Logging;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Paths;
using TMConcept.Core.Videos;

namespace TMConcept.Core.Deletion;

/// <summary>
/// Deletes produced videos (Produced videos → Delete): each one's version folder for good, then its record; its concept's folder too when
/// that was its last version. The other versions stay. A folder that cannot be removed (the player or another program holds a file) keeps
/// the record and is reported.
/// </summary>
public sealed class VideoDeletion(IAppPaths paths, IFolderDiscard discard, IVideoStore videos, ILogger<VideoDeletion> logger)
    : OwnerDeletionBase(paths, discard, videos, logger)
{
    /// <summary>The count and the bytes on disk of <paramref name="ids"/>.</summary>
    public async Task<DeletionPlan> PlanAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var all = await Videos.AllAsync(cancellationToken).ConfigureAwait(false);
        var targets = all.Where(v => ids.Contains(v.Id)).ToList();
        var bytes = await Task.Run(() => Size(targets.SelectMany(v => VideoPaths(v, all))), cancellationToken).ConfigureAwait(false);
        return new DeletionPlan(0, targets.Count, 0, bytes);
    }

    /// <summary>Deletes <paramref name="ids"/> (files first, each record after its files).</summary>
    public async Task<DeletionResult> DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var all = await Videos.AllAsync(cancellationToken).ConfigureAwait(false);
        var tally = new DeletionTally();
        await DeleteVideosAsync(all.Where(v => ids.Contains(v.Id)).ToList(), all, tally, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Delete: {Count} video(s) deleted ({Megabytes} MB), {Failed} failed", tally.Deleted, tally.Freed >> 20, tally.Failed.Count);
        return Result(tally);
    }
}
