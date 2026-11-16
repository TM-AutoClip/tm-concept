using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TMConcept.Desktop.Logging;

/// <summary>
/// Reads the kept log files for the log page: which days there are, every entry of a day from the app's and the speech worker's file (by
/// time), and what was added to a file since the last read (the live tail). The files are opened for reading while the app keeps writing
/// them (shared). The format is <see cref="LogLine"/>'s: <c>2026-11-16 14:03:21.517 +03:00 [Warning] Category[12]: message</c>, the lines of
/// an exception below it; the header line of a start (<c>=== TM Concept … ===</c>) is an entry of its own.
/// </summary>
public sealed partial class LogFileReader(string folder)
{
    /// <summary>The days with a log file, newest first.</summary>
    public IReadOnlyList<DateOnly> Days()
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(folder, "*" + LogRules.Extension)
            .Select(f => LogFileName.TryParse(Path.GetFileName(f), out var day) ? day : (DateOnly?)null)
            .OfType<DateOnly>()
            .Distinct()
            .OrderDescending()
            .ToList();
    }

    /// <summary>The files of <paramref name="day"/> with the process kind that writes each.</summary>
    public IReadOnlyList<(string File, string Source)> Files(DateOnly day) =>
        LogRules.Kinds.Select(k => (Path.Combine(folder, LogFileName.For(k, day)), k)).Where(f => File.Exists(f.Item1)).ToList();

    /// <summary>Every entry of <paramref name="day"/> (both processes), oldest first; with the length read of each file (for the tail).</summary>
    public (IReadOnlyList<LogEntry> Entries, IReadOnlyDictionary<string, long> Read) ReadDay(DateOnly day)
    {
        var entries = new List<LogEntry>();
        var read = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (file, source) in Files(day))
        {
            var (added, length) = ReadFrom(file, source, 0);
            entries.AddRange(added);
            read[file] = length;
        }

        return (entries.OrderBy(e => e.At).ToList(), read);
    }

    /// <summary>
    /// The entries written to <paramref name="file"/> after byte <paramref name="offset"/>, up to its last complete line, and where the next
    /// read starts. A file that became shorter (a new day's file of the same name cannot, but a cleaned one can) is read from its start.
    /// </summary>
    public static (IReadOnlyList<LogEntry> Entries, long Next) ReadFrom(string file, string source, long offset)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < offset)
            {
                offset = 0;
            }

            if (stream.Length == offset)
            {
                return ([], offset);
            }

            stream.Seek(offset, SeekOrigin.Begin);
            var bytes = new byte[stream.Length - offset];
            stream.ReadExactly(bytes);
            var end = Array.LastIndexOf(bytes, (byte)'\n');
            if (end < 0)
            {
                // Not even one complete line yet.
                return ([], offset);
            }

            return (Parse(Encoding.UTF8.GetString(bytes, 0, end + 1), source), offset + end + 1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ([], offset);
        }
    }

    /// <summary>The entries in <paramref name="text"/> (lines that do not start an entry belong to the one before).</summary>
    public static IReadOnlyList<LogEntry> Parse(string text, string source)
    {
        ArgumentNullException.ThrowIfNull(text);
        var entries = new List<LogEntry>();
        LogEntry? current = null;
        var detail = new StringBuilder();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (Header().Match(line) is { Success: true } header)
            {
                Flush();
                var at = DateTimeOffset.ParseExact(header.Groups[1].Value, "yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
                var id = header.Groups[4].Success ? int.Parse(header.Groups[4].Value, CultureInfo.InvariantCulture) : 0;
                current = new LogEntry(at, header.Groups[2].Value, source, header.Groups[3].Value, id, header.Groups[5].Value, string.Empty);
            }
            else if (line.StartsWith("=== ", StringComparison.Ordinal))
            {
                Flush();
                current = new LogEntry(current?.At ?? entries.LastOrDefault()?.At ?? DateTimeOffset.MinValue, "Information", source, AppIdentity.Name, 0,
                    line.Trim('=', ' '), string.Empty);
            }
            else if (line.Length > 0)
            {
                if (current is null)
                {
                    // The rest of an entry whose first line was read before (the live tail): shown on its own.
                    current = new LogEntry(entries.LastOrDefault()?.At ?? DateTimeOffset.MinValue, "Information", source, string.Empty, 0, line, string.Empty);
                }
                else
                {
                    detail.Append(detail.Length > 0 ? "\n" : string.Empty).Append(line);
                }
            }
        }

        Flush();

        // A start's header line has no time of its own: it takes the time of the entry after it.
        for (var i = entries.Count - 2; i >= 0; i--)
        {
            if (entries[i].At == DateTimeOffset.MinValue)
            {
                entries[i] = entries[i] with { At = entries[i + 1].At };
            }
        }

        return entries;

        void Flush()
        {
            if (current is null)
            {
                return;
            }

            entries.Add(detail.Length > 0 ? current with { Detail = detail.ToString() } : current);
            detail.Clear();
            current = null;
        }
    }

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(\w+)\] ([^\s\[:]*)(?:\[(\d+)\])?: (.*)$")]
    private static partial Regex Header();
}
