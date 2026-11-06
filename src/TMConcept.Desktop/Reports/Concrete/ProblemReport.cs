using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Paths;

namespace TMConcept.Desktop.Reports;

/// <summary>
/// Builds <c>tm-concept-logs-yyyyMMdd-HHmm.zip</c> in the data folder's <c>reports</c> folder: the log files still kept (the last 7 days,
/// <c>logs/…</c>), every line scrubbed once more (the app's folders, and every profile name and title of the database by value:
/// <see cref="ReportNames"/>), and <see cref="ReportRules.SystemFile"/>. Nothing else of the data folder goes in (no database, no key file,
/// no settings, no backups) and nothing is sent anywhere. Only the newest <see cref="ReportRules.KeepZips"/> zips are kept.
/// </summary>
public sealed class ProblemReport(AppPaths paths, SystemReport system, ReportNames names, LogScrubber scrubber, TimeProvider clock,
    ILogger<ProblemReport> logger)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes the zip and returns its full path.</summary>
    public async Task<string> CreateAsync(CancellationToken cancellationToken)
    {
        var known = await names.ListAsync(cancellationToken).ConfigureAwait(false);
        var masking = known is null ? scrubber : scrubber.WithNames(known);
        var now = clock.GetLocalNow();
        var today = DateOnly.FromDateTime(now.DateTime);
        Directory.CreateDirectory(paths.ReportDirectory);
        var zip = Path.Combine(paths.ReportDirectory, ReportRules.ZipPrefix + now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".zip");
        var logs = LogFiles(today);
        using (var archive = new ZipArchive(new FileStream(zip, FileMode.Create, FileAccess.Write, FileShare.None), ZipArchiveMode.Create))
        {
            foreach (var file in logs)
            {
                using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(source, Utf8);
                using var target = new StreamWriter(archive.CreateEntry("logs/" + Path.GetFileName(file), CompressionLevel.Optimal).Open(), Utf8);
                while (reader.ReadLine() is { } line)
                {
                    target.WriteLine(masking.Scrub(line));
                }
            }

            using var info = new StreamWriter(archive.CreateEntry(ReportRules.SystemFile, CompressionLevel.Optimal).Open(), Utf8);
            info.Write(masking.Scrub(system.Build() + "Log files: " + (logs.Count == 0 ? "none" : string.Join(", ", logs.Select(Path.GetFileName)))
                + Environment.NewLine + "Names masked by value: " + (known is null ? "no (the app was locked or the names could not be read)" : "yes") + Environment.NewLine));
        }

        logger.LogInformation("Problem report written ({File}, {Count} log files, {Kilobytes} KB, names masked: {Names})", Path.GetFileName(zip),
            logs.Count, new FileInfo(zip).Length / 1024, known is not null);
        Prune();
        return zip;
    }

    /// <summary>The log files the zip takes: this app's files by name, still kept by the 7-day rule, oldest first.</summary>
    private List<string> LogFiles(DateOnly today)
    {
        if (!Directory.Exists(paths.LogDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(paths.LogDirectory, "*" + LogRules.Extension)
            .Where(f => LogFileName.TryParse(Path.GetFileName(f), out var day) && LogRetention.Keeps(day, today))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Deletes the zips beyond the newest five (by the time in their name); a zip that cannot be deleted is a warning.</summary>
    private void Prune()
    {
        foreach (var old in Directory.EnumerateFiles(paths.ReportDirectory, ReportRules.ZipPrefix + "*.zip")
                     .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).Skip(ReportRules.KeepZips))
        {
            try
            {
                File.Delete(old);
                logger.LogInformation("Old problem report deleted ({File})", Path.GetFileName(old));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "An old problem report could not be deleted ({File})", Path.GetFileName(old));
            }
        }
    }
}
