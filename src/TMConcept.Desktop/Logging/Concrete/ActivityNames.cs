using System.Text.RegularExpressions;
using TMConcept.Core.Activity;

namespace TMConcept.Desktop.Logging;

/// <summary>
/// The names in the activity lines of the log page. The log file holds an activity line with <see cref="ActivityPlaceholders"/> instead of
/// the titles and profile names (they are the user's content); the log page showed them as they stand, so the developer read
/// "&lt;title&gt;: iptal edildi" (18 Nov 2026). A line of this session is matched with the same line the activity strip keeps in memory
/// (same level, a moment apart, the same text around the names) and shown with its names; an older line, whose names are gone for good,
/// says in words that the name is not kept in the log. The file itself never gets the names.
/// </summary>
public static class ActivityNames
{
    /// <summary>The start of an activity line's message in the log file (<c>ActivityLog</c> writes "Activity: {Text}").</summary>
    public const string Prefix = "Activity: ";

    /// <summary>How far apart the log file's line and the kept line may be.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// <paramref name="entry"/> as the log page shows it: an activity line with placeholders gets the names of the matching kept line of
    /// <paramref name="recent"/>, or the words <paramref name="hiddenTitle"/> / <paramref name="hiddenName"/> in their place. Every other
    /// entry is returned as it is.
    /// </summary>
    public static LogEntry Fill(LogEntry entry, IReadOnlyList<ActivityEntry> recent, string hiddenTitle, string hiddenName)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(recent);
        if (!string.Equals(entry.Category, ActivityLoggerProvider.SkippedCategory, StringComparison.Ordinal)
            || !entry.Message.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return entry;
        }

        var body = entry.Message[Prefix.Length..];
        if (!body.Contains(ActivityPlaceholders.Title, StringComparison.Ordinal) && !body.Contains(ActivityPlaceholders.Name, StringComparison.Ordinal))
        {
            return entry;
        }

        var pattern = new Regex("^" + Regex.Escape(body).Replace(ActivityPlaceholders.Title, "(.+?)", StringComparison.Ordinal)
            .Replace(ActivityPlaceholders.Name, "(.+?)", StringComparison.Ordinal) + "$", RegexOptions.Singleline | RegexOptions.CultureInvariant, MatchTimeout);
        var level = Level(entry);
        var match = recent
            .Where(r => r.Level == level && (r.At - entry.At).Duration() <= Window)
            .OrderBy(r => (r.At - entry.At).Duration())
            .FirstOrDefault(r => Matches(pattern, r.Text));
        var shown = match?.Text ?? body.Replace(ActivityPlaceholders.Title, hiddenTitle, StringComparison.Ordinal)
            .Replace(ActivityPlaceholders.Name, hiddenName, StringComparison.Ordinal);
        return entry with { Message = Prefix + shown };
    }

    private static ActivityLevel Level(LogEntry entry) => entry.Rank switch
    {
        >= 4 => ActivityLevel.Error,
        3 => ActivityLevel.Warning,
        _ => ActivityLevel.Info,
    };

    private static bool Matches(Regex pattern, string text)
    {
        try
        {
            return pattern.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            // A very long line: shown with the words instead of the names.
            return false;
        }
    }
}
