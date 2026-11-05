using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace TMConcept.Engines.Processes;

/// <summary>
/// Linux: children are tracked and their trees killed on exit (see <see cref="ChildProcessGuardBase"/>); nothing ties them to the app at
/// start. For the start-up sweep the executable is read from /proc/&lt;pid&gt;/exe (never the arguments) and the parent from
/// /proc/&lt;pid&gt;/stat; another user's processes cannot be read and are skipped.
/// </summary>
public sealed class LinuxChildProcessGuard(ILogger<LinuxChildProcessGuard> logger) : ChildProcessGuardBase(logger)
{
    /// <inheritdoc />
    protected override StringComparison PathComparison => StringComparison.Ordinal;

    /// <inheritdoc />
    protected override void Tie(Process process)
    {
        // Nothing to do: there is no job object; KillAll ends the tree on exit.
    }

    /// <inheritdoc />
    protected override string? ImagePath(int pid)
    {
        try
        {
            return new FileInfo(Folder(pid) + "/exe").LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another user's process (no permission), or it just ended: not ours to end.
            return null;
        }
    }

    /// <inheritdoc />
    protected override int? ParentId(int pid)
    {
        try
        {
            return ParseParent(File.ReadAllText(Folder(pid) + "/stat"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // It just ended.
            return null;
        }
    }

    /// <summary>The parent id from a /proc/&lt;pid&gt;/stat line ("pid (name) state ppid …"; the name may hold spaces and brackets).</summary>
    internal static int? ParseParent(string stat)
    {
        ArgumentNullException.ThrowIfNull(stat);
        var end = stat.LastIndexOf(')');
        if (end < 0)
        {
            return null;
        }

        var fields = stat[(end + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 1 && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parent) ? parent : null;
    }

    private static string Folder(int pid) => "/proc/" + pid.ToString(CultureInfo.InvariantCulture);
}
