namespace TMConcept.Core.Deletion;

/// <summary>
/// After a delete: the folders of the library that it left empty (a concept folder whose last version went, an <c>images</c> folder, a
/// profile folder) are removed, from the deleted path upwards, never the library folder itself and never a folder that still holds anything.
/// A folder that cannot be removed stays (it is empty and harmless; the next delete tries again).
/// </summary>
public static class LibraryPruner
{
    /// <summary>Removes the empty parents of each of <paramref name="removed"/> inside <paramref name="library"/>.</summary>
    public static void Prune(IEnumerable<string> removed, string library)
    {
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentException.ThrowIfNullOrEmpty(library);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(library));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var path in removed)
        {
            var folder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
            while (folder is not null && folder.Length > root.Length && folder.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            {
                try
                {
                    if (!Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any())
                    {
                        break;
                    }

                    Directory.Delete(folder);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    break;
                }

                folder = Path.GetDirectoryName(folder);
            }
        }
    }
}
