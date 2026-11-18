using TMConcept.Core.DiskSpace;
using TMConcept.Core.Documents;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// The documents as storage rows: one row per document of every profile (in use, so never cleaned up as unused), and the copies no document
/// points at any more (left behind by an interrupted add) as one leftover row that the clean-up may take. While the library adds a document
/// or makes vectors there is no leftover row (the copy being written has no record yet), and a ".part" file younger than
/// <see cref="PartAge"/> is never a leftover (an add may be writing it). 0.1.25: a PDF document's row also counts its file's kept page texts
/// (once per file), and the kept pages of a file no document holds any more (an add that was canceled) are a leftover row of their own
/// (<see cref="DocumentPagesDiskItem"/>), never while the library works (an add keeps pages before its document is saved).
/// </summary>
public sealed class DocumentDiskSource(IDocumentStore store, IProfileStore profiles, DocumentLibrary library, TimeProvider clock, Localizer text,
    IDocumentPageStore pages) : IDiskItemSource
{
    /// <summary>The key of the leftover copies' row.</summary>
    public const string LeftoverKey = "documents:leftover";

    /// <summary>How old a ".part" file must be before it counts as left behind.</summary>
    public static readonly TimeSpan PartAge = TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IDiskItem>> ListAsync(UsageMap usage, CancellationToken cancellationToken)
    {
        var documents = await store.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var names = (await profiles.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Id, p => p.Name);
        var kept = new Dictionary<(Guid Profile, string File), long>();
        foreach (var profile in names.Keys)
        {
            foreach (var file in (await pages.MarksAsync(profile, cancellationToken).ConfigureAwait(false)).GroupBy(m => (m.ProfileId, m.FileSha256)))
            {
                // Bytes in the database, about: UTF-8 text (Turkish letters take two bytes) and the row's keys.
                kept[file.Key] = file.Sum(m => (m.Characters * 11L / 10) + 100);
            }
        }

        var busy = library.Busy;
        return await Task.Run(() =>
        {
            var rows = new List<IDiskItem>();
            foreach (var orphan in kept.Where(k => !busy && !documents.Any(d => d.ProfileId == k.Key.Profile && d.FileSha256 == k.Key.File)).ToList())
            {
                rows.Add(new DocumentPagesDiskItem(orphan.Key.Profile, orphan.Key.File, text["storage.item.document_pages"], orphan.Value, pages));
            }

            var known = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var document in documents)
            {
                var path = document.StoredFile.Length > 0 ? library.FileOf(document) : Path.Combine(library.Folder, document.Id.ToString("N"));
                known.Add(Path.GetFullPath(path));
                var pageBytes = document.FileSha256.Length > 0 && kept.Remove((document.ProfileId, document.FileSha256), out var bytes) ? bytes : 0;
                rows.Add(new DocumentDiskItem(document, names.GetValueOrDefault(document.ProfileId, string.Empty), library, DiskSizes.Of(path) + pageBytes, path));
            }

            if (!library.Busy && Directory.Exists(library.Folder))
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                var now = clock.GetUtcNow().UtcDateTime;
                var leftovers = Directory.EnumerateFiles(library.Folder, "*", options)
                    .Where(f => !known.Contains(Path.GetFullPath(f)))
                    .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || now - File.GetLastWriteTimeUtc(f) >= PartAge)
                    .ToList();
                if (leftovers.Count > 0)
                {
                    rows.Add(new FolderDiskItem(LeftoverKey, text["storage.item.document_leftovers"], DiskArea.Documents, DiskSizes.Of(leftovers), leftovers, inCleanup: true, inFullDelete: true));
                }
            }

            return (IReadOnlyList<IDiskItem>)rows;
        }, cancellationToken).ConfigureAwait(false);
    }
}
