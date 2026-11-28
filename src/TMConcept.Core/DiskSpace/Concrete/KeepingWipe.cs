using TMConcept.Core.Paths;

namespace TMConcept.Core.DiskSpace;

/// <inheritdoc cref="IKeepingWipe" />
public sealed class KeepingWipe(IFolderDiscard discard) : IKeepingWipe
{
    /// <inheritdoc />
    public async Task<int> WipeAsync(string path, IReadOnlyCollection<string> keep, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(keep);
        var kept = keep.Where(k => k.Length > 0).Select(Full).ToList();
        return await WipeFullAsync(Full(path), kept, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> WipeFullAsync(string path, List<string> kept, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return 0;
        }

        if (kept.Any(k => Same(k, path) || LibraryPaths.Inside(path, k)))
        {
            return 0;
        }

        if (!kept.Any(k => LibraryPaths.Inside(k, path)))
        {
            await discard.DiscardAsync(path, cancellationToken).ConfigureAwait(false);
            return 1;
        }

        // A kept path lies inside: everything next to it goes, the folders on the way to it stay.
        var count = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(path).ToList())
        {
            count += await WipeFullAsync(Full(entry), kept, cancellationToken).ConfigureAwait(false);
        }

        return count;
    }

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool Same(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
