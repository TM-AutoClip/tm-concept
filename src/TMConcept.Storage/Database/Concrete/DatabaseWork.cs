using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace TMConcept.Storage.Database;

/// <summary>
/// How a piece of database work runs (0.1.39). Microsoft.Data.Sqlite's async methods do their work on the calling thread (SQLite has no
/// asynchronous I/O), so a store called from the window's thread would read and write there: every piece of work goes to the thread pool
/// (<see cref="RunAsync{T}"/>) and the window's thread only awaits it. Writes to one file go one at a time (<see cref="Writer"/>; reads run
/// side by side): two writers that both read first and then write would otherwise end in "database is locked" (SQLite gives up at once on
/// that deadlock, the busy wait does not help). Files are let go with <see cref="ReleaseFiles"/> before one is replaced, moved or locked.
/// </summary>
internal static class DatabaseWork
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Writers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs <paramref name="work"/> on the thread pool, never on the caller's thread (or its synchronization context).</summary>
    public static Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken) => Task.Run(work, cancellationToken);

    /// <summary>The one-writer gate of <paramref name="file"/> (one per file for the whole process).</summary>
    public static SemaphoreSlim Writer(string file) => Writers.GetOrAdd(Path.GetFullPath(file), static _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// Closes every pooled connection (each holds its file open) and checks that <paramref name="file"/> is no longer held by this process:
    /// <c>true</c> when it can be opened alone (or does not exist). A connection that is in use at that moment is closed when its work ends.
    /// </summary>
    public static bool ReleaseFiles(string? file = null)
    {
        SqliteConnection.ClearAllPools();
        if (file is null || !File.Exists(file))
        {
            return true;
        }

        try
        {
            using var alone = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
