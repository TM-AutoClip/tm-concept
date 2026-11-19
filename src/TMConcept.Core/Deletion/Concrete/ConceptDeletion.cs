using Microsoft.Extensions.Logging;
using TMConcept.Core.Concepts;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Paths;
using TMConcept.Core.Videos;

namespace TMConcept.Core.Deletion;

/// <summary>
/// Deletes trashed concepts for good (Trash → Delete permanently) with everything they generated: their videos (files and records), their
/// kept scene images (<c>work/scene-images/&lt;concept&gt;</c>), then the concept rows. Each concept is on its own: one whose files could not
/// all be removed stays in the trash and is reported, the others go. A concept that is not in the trash is never touched.
/// </summary>
public sealed class ConceptDeletion(IAppPaths paths, IFolderDiscard discard, IVideoStore videos, IConceptStore concepts, ILogger<ConceptDeletion> logger)
    : OwnerDeletionBase(paths, discard, videos, logger)
{
    /// <summary>How many of <paramref name="ids"/> are in the trash, their videos and the bytes on disk.</summary>
    public async Task<DeletionPlan> PlanAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var trashed = await TrashedAsync(ids, cancellationToken).ConfigureAwait(false);
        var all = await Videos.AllAsync(cancellationToken).ConfigureAwait(false);
        var theirs = all.Where(v => v.ConceptId is { } c && trashed.Contains(c)).ToList();
        var bytes = await Task.Run(() => Size(theirs.SelectMany(v => VideoPaths(v, all)).Concat(trashed.Select(SceneImages))), cancellationToken)
            .ConfigureAwait(false);
        return new DeletionPlan(trashed.Count, theirs.Count, 0, bytes);
    }

    /// <summary>Deletes the trashed concepts among <paramref name="ids"/>; <see cref="DeletionResult.Deleted"/> counts the concepts.</summary>
    public async Task<DeletionResult> DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var trashed = await TrashedAsync(ids, cancellationToken).ConfigureAwait(false);
        var tally = new DeletionTally();
        var deleted = 0;
        foreach (var id in trashed)
        {
            var all = await Videos.AllAsync(cancellationToken).ConfigureAwait(false);
            var complete = await DeleteVideosAsync(all.Where(v => v.ConceptId == id).ToList(), all, tally, cancellationToken).ConfigureAwait(false);
            complete &= await RemoveAsync([SceneImages(id)], tally, cancellationToken).ConfigureAwait(false);
            if (complete)
            {
                deleted += await concepts.DeletePermanentlyAsync([id], cancellationToken).ConfigureAwait(false);
            }
        }

        tally.Deleted = deleted;
        logger.LogInformation("Delete: {Count} concept(s) deleted for good with their videos ({Megabytes} MB), {Failed} file(s) failed", deleted,
            tally.Freed >> 20, tally.Failed.Count);
        return Result(tally);
    }

    private string SceneImages(Guid conceptId) => VideoPipeline.SceneImageFolder(Paths.WorkDirectory, conceptId);

    private async Task<HashSet<Guid>> TrashedAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var trashed = new HashSet<Guid>();
        foreach (var id in ids.Distinct())
        {
            if (await concepts.GetAsync(id, cancellationToken).ConfigureAwait(false) is { Status: ConceptStatus.Trashed })
            {
                trashed.Add(id);
            }
        }

        return trashed;
    }
}
