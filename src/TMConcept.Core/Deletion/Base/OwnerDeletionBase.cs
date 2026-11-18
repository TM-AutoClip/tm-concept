using Microsoft.Extensions.Logging;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Paths;
using TMConcept.Core.Videos;

namespace TMConcept.Core.Deletion;

/// <summary>
/// The common part of deleting something the user made (0.1.28): everything it generated goes with it, for good, and nothing is left
/// behind. Files first, the record after: a record never goes before its own files, so a file that cannot be removed (in use, no access)
/// keeps its record, the reason is reported and a second try finishes it. A file another record still uses (two profiles whose names give
/// the same library folder share it) is never removed. Emptied library folders go too (<see cref="LibraryPruner"/>).
/// </summary>
public abstract class OwnerDeletionBase(IAppPaths paths, IFolderDiscard discard, IVideoStore videos, ILogger logger)
{
    /// <summary>The app's paths.</summary>
    protected IAppPaths Paths { get; } = paths;

    /// <summary>Deletes for good (only inside the data and library folders).</summary>
    protected IFolderDiscard Discard { get; } = discard;

    /// <summary>The video records.</summary>
    protected IVideoStore Videos { get; } = videos;

    /// <summary>
    /// The files of <paramref name="video"/>: its version folder, or, when another record's folder is that folder or lies inside it (a
    /// version made inside an older single-folder video), only the files the app wrote there.
    /// </summary>
    protected IReadOnlyList<string> VideoPaths(VideoRecord video, IReadOnlyCollection<VideoRecord> all)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(all);
        if (Resolve(video.Folder) is not { } folder)
        {
            return [];
        }

        var shared = all.Any(o => o.Id != video.Id && Resolve(o.Folder) is { } other && (Same(other, folder) || Inside(other, folder)));
        if (!shared)
        {
            return [folder];
        }

        string[] names = [video.FileName, video.ThumbnailName, VideoFolders.VideoFile, VideoFolders.ThumbnailFile, VideoFolders.SubtitlesFile,
            VideoFolders.NarrationFile, VideoFolders.ScriptFile, VideoFolders.Source, VideoFolders.Audio];
        return names.Where(n => n.Length > 0 && n == Path.GetFileName(n)).Distinct(StringComparer.Ordinal).Select(n => Path.Combine(folder, n)).ToList();
    }

    /// <summary>
    /// Deletes the files of each of <paramref name="targets"/> and then its record (a video whose files failed keeps its record);
    /// <paramref name="all"/> is every video record (for the shared check). Returns whether every one went.
    /// </summary>
    protected async Task<bool> DeleteVideosAsync(IReadOnlyCollection<VideoRecord> targets, IReadOnlyCollection<VideoRecord> all, DeletionTally tally,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(tally);
        var complete = true;
        var left = all.ToList();
        foreach (var video in targets)
        {
            if (await RemoveAsync(VideoPaths(video, left), tally, cancellationToken).ConfigureAwait(false))
            {
                await Videos.DeleteAsync(video.Id, cancellationToken).ConfigureAwait(false);
                left.RemoveAll(v => v.Id == video.Id);
                tally.Deleted++;
            }
            else
            {
                complete = false;
            }
        }

        return complete;
    }

    /// <summary>
    /// Deletes each existing path for good (its size counts as freed), then the library folders that became empty. A failure is reported in
    /// <paramref name="tally"/> and the others go on. Returns whether every one went.
    /// </summary>
    protected async Task<bool> RemoveAsync(IReadOnlyList<string> targets, DeletionTally tally, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(tally);
        var complete = true;
        foreach (var path in targets.Where(p => Directory.Exists(p) || File.Exists(p)))
        {
            var bytes = DiskSizes.Of(path);
            try
            {
                await Discard.DiscardAsync(path, cancellationToken).ConfigureAwait(false);
                tally.Freed += bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogError(ex, "Delete: a file could not be removed; its record stays");
                tally.Failed.Add(new DeletionFailure(path, ex.Message));
                complete = false;
            }
        }

        LibraryPruner.Prune(targets, Paths.LibraryDirectory);
        return complete;
    }

    /// <summary>The bytes of the existing paths.</summary>
    protected static long Size(IEnumerable<string> targets) => DiskSizes.Of(targets.Distinct(StringComparer.Ordinal));

    /// <summary>The full path of a library-relative path; <c>null</c> when it is empty or would leave the library.</summary>
    protected string? Resolve(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(LibraryPaths.Resolve(Paths.LibraryDirectory, relative));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Delete: a stored path points outside the library folder; skipped");
            return null;
        }
    }

    /// <summary>Whether two full paths name the same file (case-insensitive on Windows).</summary>
    protected static bool Same(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Whether <paramref name="path"/> lies strictly inside <paramref name="folder"/>.</summary>
    protected static bool Inside(string path, string folder)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(folder);
        var root = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return path.Length > root.Length && path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>The result of a finished delete.</summary>
    protected static DeletionResult Result(DeletionTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);
        return new DeletionResult(tally.Deleted, tally.Freed, tally.Failed);
    }
}
