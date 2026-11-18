using Microsoft.Extensions.Logging;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Videos;

namespace TMConcept.Core.Deletion;

/// <summary>
/// Removes sample images of a profile: the copied file for good (unless another image record, of any profile, uses the same file), then the
/// record. A file that cannot be removed keeps its record and is reported.
/// </summary>
public sealed class SampleImageDeletion(IAppPaths paths, IFolderDiscard discard, IVideoStore videos, IProfileStore profiles,
    ILogger<SampleImageDeletion> logger) : OwnerDeletionBase(paths, discard, videos, logger)
{
    /// <summary>Deletes the image records <paramref name="ids"/> with their files.</summary>
    public async Task<DeletionResult> DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var tally = new DeletionTally();
        await DeleteImagesAsync(ids, tally, cancellationToken).ConfigureAwait(false);
        return Result(tally);
    }

    /// <summary>The bytes deleting the image records <paramref name="ids"/> frees (their files that no other image record uses).</summary>
    public async Task<long> SizeAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var all = await profiles.AllImagesAsync(cancellationToken).ConfigureAwait(false);
        var going = all.Where(i => ids.Contains(i.Id)).ToList();
        return await Task.Run(() => Size(Files(going, all)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The files of <paramref name="images"/> that no other image record uses (<paramref name="all"/>: every image record).</summary>
    public IReadOnlyList<string> Files(IReadOnlyCollection<ProfileImage> images, IReadOnlyCollection<ProfileImage> all)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(all);
        var going = images.Select(i => i.Id).ToHashSet();
        var kept = all.Where(i => !going.Contains(i.Id)).Select(i => Resolve(i.FileName)).OfType<string>().ToList();
        return images.Select(i => Resolve(i.FileName)).OfType<string>().Where(f => !kept.Any(k => Same(k, f))).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Deletes the image records <paramref name="ids"/> with their files into <paramref name="tally"/>; whether every one went.</summary>
    public async Task<bool> DeleteImagesAsync(IReadOnlyCollection<Guid> ids, DeletionTally tally, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(tally);
        var all = (await profiles.AllImagesAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var complete = true;
        var deleted = 0;
        foreach (var image in all.Where(i => ids.Contains(i.Id)).ToList())
        {
            if (await RemoveAsync(Files([image], all), tally, cancellationToken).ConfigureAwait(false))
            {
                await profiles.DeleteImageAsync(image.Id, cancellationToken).ConfigureAwait(false);
                all.Remove(image);
                deleted++;
            }
            else
            {
                complete = false;
            }
        }

        tally.Deleted += deleted;
        logger.LogInformation("Delete: {Count} sample image(s) removed", deleted);
        return complete;
    }
}
