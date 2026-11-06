using System.Text.RegularExpressions;

namespace TMConcept.Core.Processes;

/// <summary>
/// A short summary of a child program's output (Node, npm, Chrome, ffmpeg, tar) for an error message or a log line: the first lines that
/// look like an error, otherwise the last lines, on one line and cut to a few hundred characters. The whole output is never dumped. The
/// user's home folder becomes <c>~</c> before the cut, and the cut falls between words where it can, so a cut never leaves part of a home
/// path that the log file's scrubber could no longer recognize (the scrubber still masks the rest: other folders, e-mail addresses, keys).
/// </summary>
public static partial class ChildOutput
{
    /// <summary>The summary of <paramref name="output"/> (at most <paramref name="lines"/> lines and <paramref name="characters"/> characters).</summary>
    public static string Summary(string? output, int lines = 4, int characters = 500)
    {
        var all = Home(output ?? string.Empty).Split('\n').Select(l => l.Trim().TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        if (all.Count == 0)
        {
            return string.Empty;
        }

        var errors = all.Where(l => ErrorLike().IsMatch(l)).Distinct(StringComparer.Ordinal).Take(lines).ToList();
        var chosen = errors.Count > 0 ? errors : all.TakeLast(Math.Min(2, lines)).ToList();
        var text = string.Join(" | ", chosen);
        if (text.Length <= characters)
        {
            return text;
        }

        var space = text.LastIndexOf(' ', characters);
        return (space >= characters / 2 ? text[..space] : text[..characters]) + "…";
    }

    /// <summary>The text with the signed-in user's home folder (either slash) written as <c>~</c>.</summary>
    private static string Home(string text)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\', '/');
        return home.Length <= 3
            ? text
            : text.Replace(home, "~", StringComparison.OrdinalIgnoreCase).Replace(home.Replace('\\', '/'), "~", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"error|fail|exception|fatal|cannot|can't|could not|unable|not found|no such|denied|invalid|ERR!|panic|abort", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorLike();
}
