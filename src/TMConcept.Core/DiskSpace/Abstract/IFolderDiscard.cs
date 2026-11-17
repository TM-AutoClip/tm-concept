namespace TMConcept.Core.DiskSpace;

/// <summary>
/// Takes a folder (or file) of the data folder away: to the recycle bin / trash, or (only after the user confirmed it, when the bin cannot
/// be used) for good. A path that does not exist is nothing to do; a path outside the data folder is refused. Throws when it could not.
/// </summary>
public interface IFolderDiscard
{
    /// <summary>Whether this one deletes for good (the bin keeps nothing).</summary>
    bool Permanent { get; }

    /// <summary>Takes <paramref name="path"/> away.</summary>
    Task DiscardAsync(string path, CancellationToken cancellationToken);
}
