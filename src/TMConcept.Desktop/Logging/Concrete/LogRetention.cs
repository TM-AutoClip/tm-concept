namespace TMConcept.Desktop.Logging;

/// <summary>
/// Deletes this app's log files older than <see cref="LogRules.KeepDays"/> days, by the date in the file name (not by the time it was last
/// written). Files with any other name are left alone. A file that cannot be deleted is reported and the sweep goes on.
/// </summary>
public static class LogRetention
{
    /// <summary>Whether a log file of <paramref name="day"/> is still kept on <paramref name="today"/>.</summary>
    public static bool Keeps(DateOnly day, DateOnly today) => day >= today.AddDays(-LogRules.KeepDays);

    /// <summary>Sweeps <paramref name="directory"/>; returns the deleted file names. <paramref name="failed"/> gets each file that stayed.</summary>
    public static IReadOnlyList<string> Sweep(string directory, DateOnly today, Action<string, Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(failed);
        var deleted = new List<string>();
        if (!Directory.Exists(directory))
        {
            return deleted;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*" + LogRules.Extension))
        {
            var name = Path.GetFileName(file);
            if (!LogFileName.TryParse(name, out var day) || Keeps(day, today))
            {
                continue;
            }

            try
            {
                File.Delete(file);
                deleted.Add(name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed(name, ex);
            }
        }

        return deleted;
    }
}
