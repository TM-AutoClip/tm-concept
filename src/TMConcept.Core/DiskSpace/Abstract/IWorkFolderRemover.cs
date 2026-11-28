namespace TMConcept.Core.DiskSpace;

/// <summary>
/// Removes a work folder the app made for one job once the job is over (0.1.59): a program that was just stopped may still hold a file in it
/// for a moment (a closing CUDA process, a virus scanner), so the delete is tried a few times with a short pause before it gives up. What
/// could not go stays for the start-up sweep. Never throws for a file in use.
/// </summary>
public interface IWorkFolderRemover
{
    /// <summary>Deletes <paramref name="folder"/> with everything in it; true when it is gone (or was never there).</summary>
    /// <param name="folder">The job's own folder.</param>
    /// <param name="cancellationToken">Ends the pauses between tries (a removal after a cancel passes <see cref="CancellationToken.None"/>).</param>
    Task<bool> RemoveAsync(string folder, CancellationToken cancellationToken);
}
