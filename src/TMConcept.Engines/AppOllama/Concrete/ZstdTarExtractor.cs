using System.Formats.Tar;
using ZstdSharp;

namespace TMConcept.Engines.AppOllama;

/// <summary>
/// Unpacks a <c>.tar.zst</c> (Ollama's Linux packages) without any program of the system: ZstdSharp decodes the stream (managed, MIT) and
/// .NET's tar reader writes the entries. Every entry must land inside the target folder (a name with <c>..</c> or a rooted path is refused,
/// so is a link that points outside); on Linux the file modes of the archive are kept (the <c>ollama</c> program stays executable).
/// </summary>
public static class ZstdTarExtractor
{
    /// <summary>Unpacks <paramref name="archive"/> into <paramref name="targetFolder"/> (made when missing).</summary>
    public static async Task ExtractAsync(string archive, string targetFolder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(archive);
        ArgumentException.ThrowIfNullOrEmpty(targetFolder);
        Directory.CreateDirectory(targetFolder);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetFolder)) + Path.DirectorySeparatorChar;
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
                        switch (entry.EntryType)
                        {
                            case TarEntryType.Directory:
                                Directory.CreateDirectory(target);
                                break;
                            case TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile:
                                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                                await entry.ExtractToFileAsync(target, overwrite: true, cancellationToken).ConfigureAwait(false);
                                break;
                            case TarEntryType.SymbolicLink or TarEntryType.HardLink:
                                // The link's own target must stay inside too (libraries link to their versioned file).
                                var linked = Path.IsPathRooted(entry.LinkName) ? entry.LinkName : Path.Combine(Path.GetDirectoryName(entry.Name) ?? string.Empty, entry.LinkName);
                                _ = Inside(root, entry.EntryType == TarEntryType.HardLink ? entry.LinkName : linked);
                                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                                await entry.ExtractToFileAsync(target, overwrite: true, cancellationToken).ConfigureAwait(false);
                                break;
                            default:
                                // Global headers, long names and the like carry no file of their own.
                                break;
                        }
                    }
                }
            }
        }
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
}
