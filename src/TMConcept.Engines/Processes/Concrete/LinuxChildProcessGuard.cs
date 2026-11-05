using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TMConcept.Engines.Processes;

/// <summary>
/// Linux: children are tracked and their trees killed on exit (see <see cref="ChildProcessGuardBase"/>); nothing ties them to the app at
/// start. The command line of an orphan is read from /proc/&lt;pid&gt;/cmdline and /proc/&lt;pid&gt;/exe (another user's processes are
/// not readable and are skipped).
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
    protected override IReadOnlyList<string> CommandLine(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var folder = "/proc/" + process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var parts = new List<string>();
        try
        {
            if (new FileInfo(folder + "/exe").LinkTarget is { } exe)
            {
                parts.Add(exe);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another user's process, or it just ended.
        }

        try
        {
            parts.AddRange(File.ReadAllText(folder + "/cmdline").Split('\0', StringSplitOptions.RemoveEmptyEntries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another user's process, or it just ended.
        }

        return parts;
    }
}
