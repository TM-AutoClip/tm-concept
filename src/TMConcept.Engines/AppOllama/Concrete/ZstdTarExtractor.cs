using System.Formats.Tar;
using ZstdSharp;

namespace TMConcept.Engines.AppOllama;

/// <summary>
/// Unpacks a <c>.tar.zst</c> (Ollama's Linux packages) without any program of the system: ZstdSharp decodes the stream (managed, MIT) and
/// .NET's tar reader reads the entries. Every entry must land inside the target folder (a name with <c>..</c> or a rooted path is refused,
/// so is a link that points outside); on Linux the file modes of the archive are kept (the <c>ollama</c> program stays executable).
/// <para>
/// Links are made here, not by .NET (<see cref="TarEntry.ExtractToFileAsync"/> refuses link entries, and Ollama's packages carry versioned
/// library links such as <c>libcudart.so.12 → libcudart.so.12.8.90</c>): after every file is written, a symbolic link becomes a symbolic link
/// with the archive's own (relative) text on Linux and macOS, and a copy of the file it names on Windows (where making links needs a privilege;
/// only the tests unpack a Linux package there); a hard link becomes a copy of the file it names. A link to a file the archive does not hold
/// is a broken package (<see cref="InvalidDataException"/>) on Windows; elsewhere a symbolic link is made as it is.
/// </para>
/// </summary>
public static class ZstdTarExtractor
{
    /// <summary>
    /// Unpacks <paramref name="archive"/> into <paramref name="targetFolder"/> (made when missing). An entry whose name
    /// <paramref name="skip"/> accepts (its name inside the archive, with <c>/</c>) is not written (a graphics backend this computer does not
    /// need); a link to a skipped file is skipped too.
    /// </summary>
    public static async Task ExtractAsync(string archive, string targetFolder, CancellationToken cancellationToken, Func<string, bool>? skip = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(archive);
        ArgumentException.ThrowIfNullOrEmpty(targetFolder);
        Directory.CreateDirectory(targetFolder);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetFolder)) + Path.DirectorySeparatorChar;
        var links = new List<Link>();
        var file = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        await using (file.ConfigureAwait(false))
        {
            var zstd = new DecompressionStream(file);
            await using (zstd.ConfigureAwait(false))
            {
                var reader = new TarReader(zstd);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) is { } entry)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var target = Inside(root, entry.Name);
                        if (skip?.Invoke(Normalized(entry.Name)) == true)
                        {
                            continue;
                        }

                        switch (entry.EntryType)
                        {
                            case TarEntryType.Directory:
                                Directory.CreateDirectory(target);
                                break;
                            case TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile:
                                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                                await entry.ExtractToFileAsync(target, overwrite: true, cancellationToken).ConfigureAwait(false);
                                break;
                            case TarEntryType.SymbolicLink:
                                // A symbolic link's text is relative to its own folder; what it names must stay inside too.
                                var named = Path.IsPathRooted(entry.LinkName)
                                    ? entry.LinkName
                                    : Path.Combine(Path.GetDirectoryName(entry.Name) ?? string.Empty, entry.LinkName);
                                links.Add(new Link(target, Inside(root, named), entry.LinkName, Symbolic: true, Skipped(skip, root, Inside(root, named))));
                                break;
                            case TarEntryType.HardLink:
                                // A hard link names its file from the archive's top.
                                var source = Inside(root, entry.LinkName);
                                links.Add(new Link(target, source, entry.LinkName, Symbolic: false, Skipped(skip, root, source)));
                                break;
                            default:
                                // Global headers, long names and the like carry no file of their own.
                                break;
                        }
                    }
                }
            }
        }

        MakeLinks(links);
    }

    /// <summary>The full path of an entry inside <paramref name="root"/>; throws <see cref="InvalidDataException"/> when it would lie outside.</summary>
    internal static string Inside(string root, string name)
    {
        if (string.IsNullOrEmpty(name) || Path.IsPathRooted(name))
        {
            throw new InvalidDataException("The archive has an entry with a rooted or empty name.");
        }

        var full = Path.GetFullPath(Path.Combine(root, name));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(root, comparison) && !string.Equals(full + Path.DirectorySeparatorChar, root, comparison))
        {
            throw new InvalidDataException("The archive has an entry outside its folder: " + name);
        }

        return full;
    }

    /// <summary>An entry name with <c>/</c> and without a leading <c>./</c>.</summary>
    internal static string Normalized(string name)
    {
        var normalized = name.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static bool Skipped(Func<string, bool>? skip, string root, string full) =>
        skip is not null && skip(Path.GetRelativePath(root, full).Replace('\\', '/'));

    /// <summary>
    /// Makes the links once every file is there (a link may come before its file in the archive). On Windows a link to a link is copied once
    /// the link it names was copied, so the passes go on while one makes progress.
    /// </summary>
    private static void MakeLinks(List<Link> links)
    {
        var pending = links.Where(l => !l.Skipped).ToList();
        while (pending.Count > 0)
        {
            var left = new List<Link>();
            foreach (var link in pending)
            {
                if (link.Symbolic && !OperatingSystem.IsWindows())
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(link.Destination)!);
                    Remove(link.Destination);
                    File.CreateSymbolicLink(link.Destination, link.Text);
                    continue;
                }

                if (!File.Exists(link.Source))
                {
                    left.Add(link);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(link.Destination)!);
                Remove(link.Destination);
                File.Copy(link.Source, link.Destination);
            }

            if (left.Count == pending.Count)
            {
                throw new InvalidDataException("The archive has a link to a file it does not hold: " + left[0].Text);
            }

            pending = left;
        }
    }

    private static void Remove(string path)
    {
        if (File.Exists(path) || new FileInfo(path).LinkTarget is not null)
        {
            File.Delete(path);
        }
    }

    /// <summary>A link to make after the files: where it goes, the file it names, the archive's own link text.</summary>
    private sealed record Link(string Destination, string Source, string Text, bool Symbolic, bool Skipped);
}
