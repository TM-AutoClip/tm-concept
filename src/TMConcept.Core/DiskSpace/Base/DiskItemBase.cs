namespace TMConcept.Core.DiskSpace;

/// <summary>
/// The common part of a storage row: its facts are given when it is listed; removing it discards each of its folders that still exists, in
/// order (a failure stops and goes up; what was taken stays taken, a second try skips it).
/// </summary>
public abstract class DiskItemBase(string key, string name, DiskArea area, DiskItemState state, long bytes, IReadOnlyList<string> paths) : IDiskItem
{
    /// <inheritdoc />
    public string Key { get; } = key;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public DiskArea Area { get; } = area;

    /// <inheritdoc />
    public DiskItemState State { get; } = state;

    /// <inheritdoc />
    public long Bytes { get; } = bytes;

    /// <inheritdoc />
    public virtual IReadOnlyList<DiskUse> UsedBy => [];

    /// <inheritdoc />
    public IReadOnlyList<string> Paths { get; } = paths;

    /// <inheritdoc />
    public virtual bool Removable => Paths.Count > 0;

    /// <inheritdoc />
    public abstract bool InCleanup { get; }

    /// <inheritdoc />
    public abstract bool InFullDelete { get; }

    /// <inheritdoc />
    public virtual async Task RemoveAsync(IFolderDiscard discard, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(discard);
        foreach (var path in Paths.Where(p => Directory.Exists(p) || File.Exists(p)))
        {
            await discard.DiscardAsync(path, cancellationToken).ConfigureAwait(false);
        }
    }
}
