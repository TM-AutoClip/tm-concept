namespace TMConcept.Core.DiskSpace;

/// <summary>
/// Deletes a folder or file the app made, for good (0.1.28: there is no recycle bin step any more; every delete is confirmed by the user
/// with its size and "cannot be undone" first). Only a path strictly inside the data folder or the library folder is taken (a root itself or
/// anything outside is refused with <see cref="InvalidOperationException"/>); a path that does not exist is nothing to do. Throws when it
/// could not (a file in use, no access).
/// </summary>
public interface IFolderDiscard
{
    /// <summary>Deletes <paramref name="path"/>.</summary>
    Task DiscardAsync(string path, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="path"/> lies strictly inside a folder this one may delete in.</summary>
    bool Owns(string path);
}
