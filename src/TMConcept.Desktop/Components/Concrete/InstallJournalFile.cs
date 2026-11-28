using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Components;
using TMConcept.Desktop.Paths;

namespace TMConcept.Desktop.Components;

/// <summary>
/// <see cref="IInstallJournal"/> as <c>install-journal.json</c> in the data folder (a list of component codes), next to the components
/// folder and not inside it: "Reinstall from scratch" deletes that folder first. Written to a temporary file and moved over the old one, so
/// a cut-off write never leaves half a list; the file goes when the list is empty.
/// </summary>
public sealed class InstallJournalFile(AppPaths paths, ILogger<InstallJournalFile> logger) : IInstallJournal
{
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public IReadOnlyList<string> Unfinished()
    {
        lock (_gate)
        {
            return Read();
        }
    }

    /// <inheritdoc />
    public void Begin(IEnumerable<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        lock (_gate)
        {
            var entries = Read();
            var added = codes.Where(c => c.Length > 0 && !entries.Contains(c, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
            if (added.Count == 0)
            {
                return;
            }

            entries.AddRange(added);
            Write(entries);
            logger.LogInformation("Install journal: {Codes} begun", string.Join(", ", added));
        }
    }

    /// <inheritdoc />
    public void Finished(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        lock (_gate)
        {
            var entries = Read();
            if (entries.RemoveAll(e => e == code) > 0)
            {
                Write(entries);
            }
        }
    }

    private List<string> Read()
    {
        if (!File.Exists(paths.InstallJournalFile))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(paths.InstallJournalFile), DesktopJsonContext.Default.ListString) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // An unreadable journal: nothing is resumed; each component is still offered when a step needs it.
            logger.LogWarning(ex, "The install journal could not be read; nothing is resumed from it");
            return [];
        }
    }

    private void Write(List<string> entries)
    {
        if (entries.Count == 0)
        {
            File.Delete(paths.InstallJournalFile);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(paths.InstallJournalFile)!);
        var temporary = paths.InstallJournalFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries, DesktopJsonContext.Default.ListString));
        File.Move(temporary, paths.InstallJournalFile, overwrite: true);
    }
}
