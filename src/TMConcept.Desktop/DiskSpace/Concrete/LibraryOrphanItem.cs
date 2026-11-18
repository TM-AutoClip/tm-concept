using TMConcept.Core.DiskSpace;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// A storage row of library files no record points at (<see cref="LibraryOrphanSource"/>). Neither clean-up takes it: only ticking it does
/// (a delete before 0.1.28 kept such video folders on purpose). Removing it asks <see cref="LibraryOrphanSource"/> again for each path first
/// and skips one a record points at by now (a video that finished after the page was listed), then deletes the rest for good.
/// </summary>
public sealed class LibraryOrphanItem(string key, string name, long bytes, IReadOnlyList<string> paths, Func<CancellationToken, Task<IReadOnlySet<string>>> stillOrphans)
    : DiskItemBase(key, name, DiskArea.Library, DiskItemState.Leftover, bytes, paths)
{
    /// <inheritdoc />
    public override bool InCleanup => false;

    /// <inheritdoc />
    public override bool InFullDelete => false;

    /// <inheritdoc />
    public override async Task RemoveAsync(IFolderDiscard discard, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(discard);
        var orphans = await stillOrphans(cancellationToken).ConfigureAwait(false);
        foreach (var path in Paths.Where(p => orphans.Contains(Path.GetFullPath(p)) && (Directory.Exists(p) || File.Exists(p))))
        {
            await discard.DiscardAsync(path, cancellationToken).ConfigureAwait(false);
        }
    }
}
