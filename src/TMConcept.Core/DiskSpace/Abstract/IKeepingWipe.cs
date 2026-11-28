namespace TMConcept.Core.DiskSpace;

/// <summary>
/// Deletes a folder or file for good (through <see cref="IFolderDiscard"/>) except what must stay inside it (28 Nov 2026): a component's old
/// files before its download while another component's folder lies inside them. A path that is one of the kept ones, or lies inside one, is left alone; a folder that holds a kept one loses everything else.
/// </summary>
public interface IKeepingWipe
{
    /// <summary>
    /// Deletes <paramref name="path"/> without <paramref name="keep"/>; returns how many paths were handed to the delete. Throws like
    /// <see cref="IFolderDiscard.DiscardAsync"/> when one could not be deleted (what went before stays deleted).
    /// </summary>
    Task<int> WipeAsync(string path, IReadOnlyCollection<string> keep, CancellationToken cancellationToken);
}
