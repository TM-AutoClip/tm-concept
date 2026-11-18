using Microsoft.Extensions.Logging;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Paths;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// The common part of a discard: only a path strictly inside the data folder or the library folder is taken (anything else, the two folders
/// themselves and a folder that holds one of them included, is refused with <see cref="InvalidOperationException"/>: a wrong path never reaches a delete); a path that is gone
/// already is nothing to do; each one is logged relative to its root (never the user's folders).
/// </summary>
public abstract class FolderDiscardBase(IAppPaths paths, ILogger logger) : IFolderDiscard
{
    /// <inheritdoc />
    public async Task DiscardAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = RootOf(full) ?? throw new InvalidOperationException("Only a folder inside the app's data folder or video library can be removed.");
        if (!Directory.Exists(full) && !File.Exists(full))
        {
            return;
        }

        await TakeAsync(full, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("{Path} deleted permanently", Path.GetRelativePath(root, full));
    }

    /// <inheritdoc />
    public bool Owns(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return RootOf(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))) is not null;
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

    /// <summary>Deletes the existing <paramref name="path"/>; throws when it could not.</summary>
    protected abstract Task TakeAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// The root (data folder or library folder) <paramref name="full"/> lies strictly inside; <c>null</c> when none, and when it is a root or
    /// holds one (a portable copy keeps the library inside the data folder: the library folder itself is never taken).
    /// </summary>
    private string? RootOf(string full)
    {
        string[] roots = [paths.DataDirectory, paths.LibraryDirectory];
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (roots.Any(r => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(r)), full, comparison) || Inside(r, full)))
        {
            return null;
        }

        return roots.FirstOrDefault(r => Inside(full, r));
    }
}
