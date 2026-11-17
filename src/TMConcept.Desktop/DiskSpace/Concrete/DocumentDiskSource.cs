using TMConcept.Core.DiskSpace;
using TMConcept.Core.Documents;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.DiskSpace;

/// <summary>
/// The documents as storage rows: one row per document of every profile (in use, so never cleaned up as unused), and the copies no document
/// points at any more (left behind by an interrupted add) as one leftover row that the clean-up may take.
/// </summary>
public sealed class DocumentDiskSource(IDocumentStore store, IProfileStore profiles, DocumentLibrary library, Localizer text) : IDiskItemSource
{
    /// <summary>The key of the leftover copies' row.</summary>
    public const string LeftoverKey = "documents:leftover";

    /// <inheritdoc />
    public async Task<IReadOnlyList<IDiskItem>> ListAsync(UsageMap usage, CancellationToken cancellationToken)
    {
        var documents = await store.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var names = (await profiles.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Id, p => p.Name);
        return await Task.Run(() =>
        {
            var rows = new List<IDiskItem>();
            var known = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var document in documents)
            {
                var path = document.StoredFile.Length > 0 ? library.FileOf(document) : Path.Combine(library.Folder, document.Id.ToString("N"));
                known.Add(Path.GetFullPath(path));
                rows.Add(new DocumentDiskItem(document, names.GetValueOrDefault(document.ProfileId, string.Empty), library, DiskSizes.Of(path), path));
            }

            if (Directory.Exists(library.Folder))
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                var leftovers = Directory.EnumerateFiles(library.Folder, "*", options).Where(f => !known.Contains(Path.GetFullPath(f))).ToList();
                if (leftovers.Count > 0)
                {
                    rows.Add(new FolderDiskItem(LeftoverKey, text["storage.item.document_leftovers"], DiskArea.Documents, DiskSizes.Of(leftovers), leftovers, inCleanup: true, inFullDelete: true));
                }
            }

            return (IReadOnlyList<IDiskItem>)rows;
        }, cancellationToken).ConfigureAwait(false);
    }
}
