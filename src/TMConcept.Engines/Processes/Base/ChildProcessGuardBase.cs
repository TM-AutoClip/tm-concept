using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TMConcept.Engines.Processes;

/// <summary>
/// No child process (ffmpeg, Node, the browser Node starts) outlives the app. Every process the app starts is handed to
/// <see cref="Adopt"/> right after it starts. Windows ties it to a job object that ends the whole job when the app's handle closes, also
/// when the app is killed or crashes. Linux has no such tie for children of a .NET <see cref="Process"/> (PR_SET_PDEATHSIG must be set
/// in the child before exec), so the tree of every tracked child is killed on exit (<see cref="KillAll"/>: normal close, an unhandled
/// exception, the process-exit event). For what a hard kill on Linux leaves behind, <see cref="SweepOrphans"/> runs at the next start: it
/// ends only orphans, processes whose executable file lies inside this app's tools folder (Node, ffmpeg, Chrome from there) and whose
/// parent is gone (or is not this app and not another tool). Arguments are never looked at: an editor or a file manager that merely has a
/// tools path on its command line is not touched.
/// </summary>
public abstract class ChildProcessGuardBase(ILogger logger) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly HashSet<Process> _live = [];
    private bool _disposed;

    /// <summary>How paths compare on this system (Windows ignores case).</summary>
    protected abstract StringComparison PathComparison { get; }

    /// <summary>
    /// Takes a just-started process; dispose the result when the process is done (before disposing the process). Never throws: a process
    /// that cannot be tied is logged and still killed by <see cref="KillAll"/>.
    /// </summary>
    public IDisposable Adopt(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            Tie(process);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "A child process ({Program}) could not be tied to the app; it is ended on exit instead", Program(process));
        }

        lock (_gate)
        {
            _live.Add(process);
        }

        return new Adoption(this, process);
    }

    /// <summary>Ends the tree of every tracked child that still runs (closing the app); returns how many were running.</summary>
    public int KillAll()
    {
        Process[] live;
        lock (_gate)
        {
            live = [.. _live];
            _live.Clear();
        }

        var killed = live.Count(p => TryKillTree(p, "exit"));
        if (killed > 0)
        {
            logger.LogWarning("{Count} child process(es) still ran when the app closed and were ended", killed);
        }

        return killed;
    }

    /// <summary>
    /// Start of the app (after the data folder's instance lock is held): ends the orphans an earlier run left behind. A process is ended
    /// when its executable lies inside <paramref name="toolsDirectory"/> and its parent is gone, or is neither this app nor another program
    /// from the tools folder (Chrome under Node is ended with Node's tree). Returns how many were ended.
    /// </summary>
    public int SweepOrphans(string toolsDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolsDirectory);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(toolsDirectory)) + Path.DirectorySeparatorChar;
        var own = Environment.ProcessId;
        var app = Path.GetFileName(Environment.ProcessPath ?? string.Empty);
        bool InTools(string? image) => image is not null && image.StartsWith(root, PathComparison);
        var ended = 0;
        foreach (var pid in ProcessIds())
        {
            if (pid == own || !InTools(ImagePath(pid)))
            {
                continue;
            }

            if (ParentId(pid) is { } parent && parent != 0 && (parent == own || (ImagePath(parent) is { } parentImage
                && (InTools(parentImage) || string.Equals(Path.GetFileName(parentImage), app, PathComparison)))))
            {
                // A tool's child (ended with its parent's tree) or a child of a running TM Concept: not an orphan.
                continue;
            }

            if (EndOrphan(pid))
            {
                ended++;
            }
        }

        return ended;
    }
    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Whether the process is tied to the app by the system (Windows job); false where there is no such tie.</summary>
    internal virtual bool IsTied(Process process) => false;

    /// <summary>Ties a just-started process to the app where the system can (Windows job object).</summary>
    protected abstract void Tie(Process process);

    /// <summary>The full path of a process's executable file; null when it cannot be read (another user's process, gone).</summary>
    protected abstract string? ImagePath(int pid);

    /// <summary>The parent process id; null when it cannot be read.</summary>
    protected abstract int? ParentId(int pid);

    /// <summary>Ends an orphan's process tree (logged); false when it was already gone or could not be ended.</summary>
    protected virtual bool EndOrphan(int pid)
    {
        using var process = Open(pid);
        if (process is null)
        {
            return false;
        }

        logger.LogWarning("A process left by an earlier run ({Program}, pid {Pid}) is ended", Program(process), pid);
        return TryKillTree(process, "sweep");
    }

    /// <summary>Every running process id.</summary>
    protected virtual IEnumerable<int> ProcessIds()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                yield return process.Id;
            }
        }
    }

    /// <summary>Ends what still runs.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (disposing)
        {
            KillAll();
        }
    }

    /// <summary>The program's file name for the log (never the full path: it would name the user's folders).</summary>
    private static string Program(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch (InvalidOperationException)
        {
            return "?";
        }
    }

    private static Process? Open(int pid)
    {
        try
        {
            return Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            // It ended meanwhile.
            return null;
        }
    }

    private void Release(Process process)
    {
        lock (_gate)
        {
            _live.Remove(process);
        }
    }

    private bool TryKillTree(Process process, string when)
    {
        try
        {
            if (process.HasExited)
            {
                return false;
            }

            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (InvalidOperationException)
        {
            // It ended (or was released) between the check and the kill.
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or NotSupportedException or AggregateException)
        {
            logger.LogError(ex, "A child process ({Program}) could not be ended ({When})", Program(process), when);
            return false;
        }
    }

    /// <summary>Removes the process from the tracked set when the caller is done with it.</summary>
    private sealed class Adoption(ChildProcessGuardBase guard, Process process) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
            {
                guard.Release(process);
            }
        }
    }
}
