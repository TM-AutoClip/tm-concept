using System.Text.RegularExpressions;
using TMConcept.Core.Security;

namespace TMConcept.Desktop.Logging;

/// <summary>
/// The last guard before a line reaches a log file or a report: the user's home folder becomes <c>~</c> (with either slash, and as escaped
/// in JSON), e-mail addresses become <c>&lt;e-mail&gt;</c>, and tokens and keys are masked by <see cref="Redactor"/> (Bearer / Basic headers,
/// provider keys, secret query values and JSON fields). Code does not log these on purpose; this catches what a tool or a provider echoes.
/// </summary>
public sealed partial class LogScrubber
{
    private readonly string[] _homes;

    /// <summary>Masks <paramref name="home"/> (empty or a drive root: no folder is masked).</summary>
    public LogScrubber(string home)
    {
        var trimmed = (home ?? string.Empty).TrimEnd('\\', '/');
        _homes = trimmed.Length > 3
            ? new[] { trimmed, trimmed.Replace('\\', '/'), trimmed.Replace('/', '\\'), trimmed.Replace("\\", "\\\\", StringComparison.Ordinal) }
                .Distinct(StringComparer.Ordinal).OrderByDescending(h => h.Length).ToArray()
            : [];
    }

    /// <summary>The scrubber of the signed-in user.</summary>
    public static LogScrubber ForCurrentUser { get; } = new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>The text with the home folder, e-mail addresses and secrets masked.</summary>
    public string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = text;
        foreach (var home in _homes)
        {
            result = result.Replace(home, "~", StringComparison.OrdinalIgnoreCase);
        }

        result = Email().Replace(result, "<e-mail>");
        return Redactor.Redact(result);
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9._%+-])[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}(?![A-Za-z0-9-])")]
    private static partial Regex Email();
}
