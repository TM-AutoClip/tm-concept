using Microsoft.Extensions.Logging;
using TMConcept.Core.Concepts;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Documents;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Videos;

namespace TMConcept.Core.Deletion;

/// <summary>
/// Deletes a profile for good with everything it generated: its videos (files and records) and its channel photo (a file
/// another profile still uses stays: two profiles whose names give the same library folder share it, so the profile's library folder is
/// never deleted as a whole, only what it owns and then the folders left empty), its document copies, the kept scene images of its
/// concepts, and last the profile row (its concepts, queue and the rest go with it in the database). When a file cannot be removed (in use)
/// the profile row STAYS with its document copies and the failures are reported; a second try finishes it.
/// </summary>
public sealed class ProfileDeletion(IAppPaths paths, IFolderDiscard discard, IVideoStore videos, IProfileStore profiles, IConceptStore concepts,
    DocumentLibrary documents, ILogger<ProfileDeletion> logger) : OwnerDeletionBase(paths, discard, videos, logger)
{
    /// <summary>What deleting the profile <paramref name="profileId"/> takes: concepts, videos, documents and the bytes on disk.</summary>
    public async Task<DeletionPlan> PlanAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var all = await Videos.AllAsync(cancellationToken).ConfigureAwait(false);
        var theirs = all.Where(v => v.ProfileId == profileId).ToList();
        var conceptIds = await ConceptIdsAsync(profileId, cancellationToken).ConfigureAwait(false);
        var docs = await documents.ListAsync(profileId, cancellationToken).ConfigureAwait(false);
        var photo = await PhotoAsync(profileId, cancellationToken).ConfigureAwait(false);
        var bytes = await Task.Run(() => Size(theirs.SelectMany(v => VideoPaths(v, all)).Concat(photo)
            .Append(DocumentFolder(profileId)).Concat(conceptIds.Select(SceneImages))), cancellationToken).ConfigureAwait(false);
        return new DeletionPlan(conceptIds.Count, theirs.Count, docs.Count, bytes);
    }

    /// <summary>Deletes the profile's files and then the profile; <see cref="DeletionResult.Deleted"/> is 1 when the profile went.</summary>
    public async Task<DeletionResult> DeleteAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var tally = new DeletionTally();
        var all = await Videos.AllAsync(cancellationToken).ConfigureAwait(false);
        var complete = await DeleteVideosAsync(all.Where(v => v.ProfileId == profileId).ToList(), all, tally, cancellationToken).ConfigureAwait(false);
        complete &= await RemoveAsync(await PhotoAsync(profileId, cancellationToken).ConfigureAwait(false), tally, cancellationToken).ConfigureAwait(false);
        var conceptIds = await ConceptIdsAsync(profileId, cancellationToken).ConfigureAwait(false);
        complete &= await RemoveAsync(conceptIds.Select(SceneImages).ToList(), tally, cancellationToken).ConfigureAwait(false);

        // The document copies go only when everything before went: a profile that stays (a file was in use) keeps its documents and their
        // records together, so the user can keep using it instead of trying again.
        if (complete)
        {
            try
            {
                await documents.DiscardProfileAsync(profileId, Discard, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogError(ex, "Delete: the profile's document copies could not be removed; the profile stays");
                tally.Failed.Add(new DeletionFailure(DocumentFolder(profileId), ex.Message));
                complete = false;
            }
        }

        tally.Deleted = 0;
        if (complete)
        {
            await profiles.DeleteAsync(profileId, cancellationToken).ConfigureAwait(false);
            tally.Deleted = 1;
        }

        logger.LogInformation("Delete: profile {Outcome} ({Megabytes} MB freed, {Failed} file(s) failed)", complete ? "deleted" : "kept", tally.Freed >> 20,
            tally.Failed.Count);
        return Result(tally);
    }

    /// <summary>The channel photo's file, unless another profile points at the same copy.</summary>
    private async Task<IReadOnlyList<string>> PhotoAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var all = await profiles.ListAsync(cancellationToken).ConfigureAwait(false);
        if (all.FirstOrDefault(p => p.Id == profileId)?.ChannelPhoto is not { Length: > 0 } stored || Resolve(stored) is not { } file)
        {
            return [];
        }

        return all.Any(p => p.Id != profileId && Resolve(p.ChannelPhoto) is { } other && Same(other, file)) ? [] : [file];
    }

    private async Task<IReadOnlyList<Guid>> ConceptIdsAsync(Guid profileId, CancellationToken cancellationToken) =>
        (await concepts.ListAsync(profileId, Enum.GetValues<ConceptStatus>(), false, cancellationToken).ConfigureAwait(false)).Select(c => c.Id).ToList();

    private string DocumentFolder(Guid profileId) => Path.Combine(documents.Folder, profileId.ToString("N"));

    private string SceneImages(Guid conceptId) => VideoPipeline.SceneImageFolder(Paths.WorkDirectory, conceptId);
}
