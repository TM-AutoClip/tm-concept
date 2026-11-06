using System.Globalization;

namespace TMConcept.Desktop.Logging;

/// <summary>Names of the daily log files: <c>&lt;kind&gt;-yyyy-MM-dd.log</c> (local date), and the older <c>tmconcept-yyyyMMdd.log</c>.</summary>
public static class LogFileName
{
    /// <summary>The file name of <paramref name="kind"/> on <paramref name="day"/>.</summary>
    public static string For(string kind, DateOnly day) => kind + "-" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + LogRules.Extension;

    /// <summary>
    /// The day of a log file of this app, by its name only; false for any other file (a file of an unknown kind, a report, a file the user put
    /// there), which is then never touched.
    /// </summary>
    public static bool TryParse(string fileName, out DateOnly day)
    {
        day = default;
        if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(LogRules.Extension, StringComparison.Ordinal))
        {
            return false;
        }

        var stem = fileName[..^LogRules.Extension.Length];
        foreach (var kind in LogRules.Kinds)
        {
            if (stem.Length == kind.Length + 11 && stem.StartsWith(kind + "-", StringComparison.Ordinal)
                && DateOnly.TryParseExact(stem[(kind.Length + 1)..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
            {
                return true;
            }
        }

        return stem.Length == LogRules.LegacyPrefix.Length + 8 && stem.StartsWith(LogRules.LegacyPrefix, StringComparison.Ordinal)
            && DateOnly.TryParseExact(stem[LogRules.LegacyPrefix.Length..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }
}
