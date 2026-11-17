using Microsoft.Extensions.Logging;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Paths;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// The common part of a discard: only a path strictly inside the data folder is taken (anything else, the data folder itself included, is
/// refused with <see cref="InvalidOperationException"/>: a wrong path never reaches the bin or a delete); a path that is gone already is
/// nothing to do; each one is logged without the user's folders.
/// </summary>
public abstract class FolderDiscardBase(IAppPaths paths, ILogger logger) : IFolderDiscard
{
    /// <inheritdoc />
    public abstract bool Permanent { get; }

    /// <inheritdoc />
    public async Task DiscardAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Inside(full, paths.DataDirectory))
        {
            throw new InvalidOperationException("Only a folder inside the app's data folder can be removed.");
        }

        if (!Directory.Exists(full) && !File.Exists(full))
        {
            return;
        }

        await TakeAsync(full, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("{Path} {Way}", Path.GetRelativePath(paths.DataDirectory, full), Permanent ? "deleted permanently" : "moved to the recycle bin");
    }

    /// <summary>Whether <paramref name="path"/> lies strictly inside <paramref name="folder"/>.</summary>
    public static bool Inside(string path, string folder)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(folder);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.Length > root.Length && full.StartsWith(root, comparison);
    }

    /// <summary>Takes the existing <paramref name="path"/> away; throws when it could not.</summary>
    protected abstract Task TakeAsync(string path, CancellationToken cancellationToken);
}
