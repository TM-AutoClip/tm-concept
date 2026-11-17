namespace TMConcept.Core.DiskSpace;

/// <summary>
/// Space no component owns, as a storage row (leftover downloads, work caches, old components folders): a name, its folders and whether the
/// clean-up and the full delete take it.
/// </summary>
public sealed class FolderDiskItem(string key, string name, DiskArea area, long bytes, IReadOnlyList<string> paths, bool inCleanup, bool inFullDelete)
    : DiskItemBase(key, name, area, DiskItemState.Leftover, bytes, paths)
{
    /// <inheritdoc />
    public override bool InCleanup { get; } = inCleanup;

    /// <inheritdoc />
    public override bool InFullDelete { get; } = inFullDelete;
}
