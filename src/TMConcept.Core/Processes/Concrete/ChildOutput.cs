using System.Text.RegularExpressions;

namespace TMConcept.Core.Processes;

/// <summary>
/// A short summary of a child program's output (Node, npm, Chrome, ffmpeg, tar) for an error message or a log line: the first lines that
/// look like an error, otherwise the last lines, on one line and cut to a few hundred characters. The whole output is never dumped; the log
/// file still scrubs what is written (home folder, e-mail addresses, keys).
/// </summary>
public static partial class ChildOutput
{
    /// <summary>The summary of <paramref name="output"/> (at most <paramref name="lines"/> lines and <paramref name="characters"/> characters).</summary>
    public static string Summary(string? output, int lines = 4, int characters = 500)
    {
        var all = (output ?? string.Empty).Split('\n').Select(l => l.Trim().TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        if (all.Count == 0)
        {
            return string.Empty;
        }

        var errors = all.Where(l => ErrorLike().IsMatch(l)).Distinct(StringComparer.Ordinal).Take(lines).ToList();
        var chosen = errors.Count > 0 ? errors : all.TakeLast(Math.Min(2, lines)).ToList();
        var text = string.Join(" | ", chosen);
        return text.Length <= characters ? text : text[..characters] + "…";
    }

    [GeneratedRegex(@"error|fail|exception|fatal|cannot|can't|could not|unable|not found|no such|denied|invalid|ERR!|panic|abort", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorLike();
}
