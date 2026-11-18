using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.Tools;

namespace TMConcept.Documents.Pdf;

/// <summary>
/// PDFium as installed on this computer (0.1.27): <c>&lt;tools&gt;/pdfium/&lt;version&gt;/</c> with the library (<c>pdfium.dll</c> or
/// <c>libpdfium.so</c>), its <c>LICENSE</c> and <c>licenses/</c> texts and a <c>.installed</c> marker written last. The pinned archive of this
/// computer's runtime id (<see cref="PdfiumRelease"/>) is downloaded only after the user agreed, checked against its SHA-256 and unpacked in
/// the app (gzip and tar, no outside program); only the library and the licence texts are taken, a name that would leave the folder stops the
/// install. Windows x64, Linux x64 and Linux ARM64 have a build.
/// </summary>
public sealed class PdfiumStore(IAppPaths paths, IToolPlatform platform, IArchiveDownloader downloader, ILogger<PdfiumStore> logger)
{
    /// <summary>The file written after a complete install.</summary>
    public const string Marker = ".installed";

    /// <summary>The licence texts' folder inside the archive and on disk.</summary>
    private const string LicensesFolder = "licenses";

    /// <summary>The pinned archive of this computer (null: no build for it).</summary>
    public ToolArchive? Archive => PdfiumRelease.Archives.FirstOrDefault(a => a.Rid == platform.Rid);

    /// <summary>Whether this computer has a build.</summary>
    public bool Supported => Archive is not null;

    /// <summary>The install folder.</summary>
    public string Folder => Path.Combine(paths.ToolsDirectory, "pdfium", PdfiumRelease.Version);

    /// <summary>The library file.</summary>
    public string LibraryFile => Path.Combine(Folder, Path.GetFileName(Archive?.ExecutablePath ?? "pdfium.dll"));

    /// <summary>Whether it is installed completely.</summary>
    public bool Ready => Supported && File.Exists(LibraryFile) && File.Exists(Path.Combine(Folder, Marker));

    /// <summary>Downloads, checks and unpacks the build (network; only after the user agreed).</summary>
    public async Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var archive = Archive ?? throw new InvalidOperationException("There is no PDFium build for " + platform.Rid + ".");
        var root = Path.GetDirectoryName(Folder)!;
        Directory.CreateDirectory(root);
        var download = Path.Combine(root, "pdfium-" + archive.Rid + ".tgz");
        await downloader.DownloadAsync(archive, download, progress, cancellationToken).ConfigureAwait(false);
        var staging = Folder + ".new";
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            await using (var stream = new FileStream(download, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
            {
                Unpack(stream, staging, archive.ExecutablePath);
            }

            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }

            Directory.Move(staging, Folder);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(LibraryFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            await File.WriteAllTextAsync(Path.Combine(Folder, Marker), PdfiumRelease.Version, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("PDFium {Version} installed ({Rid})", PdfiumRelease.Version, archive.Rid);
        }
        finally
        {
            File.Delete(download);
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    /// <summary>
    /// Unpacks a gzip-compressed tar of the release into <paramref name="folder"/>: the entry <paramref name="library"/> (to the folder's top),
    /// <c>LICENSE</c> and the files of <c>licenses/</c>; every other entry is left. Returns the files written (relative paths).
    /// <see cref="InvalidDataException"/> when a name would leave the folder (an absolute path, <c>..</c>) or the library is not in it.
    /// </summary>
    public static IReadOnlyList<string> Unpack(Stream tgz, string folder, string library)
    {
        ArgumentNullException.ThrowIfNull(tgz);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentException.ThrowIfNullOrEmpty(library);
        Directory.CreateDirectory(folder);
        var written = new List<string>();
        var hasLibrary = false;
        using var gzip = new GZipStream(tgz, CompressionMode.Decompress, leaveOpen: true);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } entry)
        {
            var name = entry.Name.Replace('\\', '/');
            while (name.StartsWith("./", StringComparison.Ordinal))
            {
                name = name[2..];
            }

            if (name.StartsWith('/') || Path.IsPathRooted(name) || name.Split('/').Any(s => s == ".."))
            {
                throw new InvalidDataException("The PDFium archive has an entry outside its folder: " + entry.Name);
            }

            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
            {
                continue;
            }

            string? target = name == library ? Path.GetFileName(library)
                : name == "LICENSE" ? "LICENSE"
                : name.StartsWith(LicensesFolder + "/", StringComparison.Ordinal) && name.Count(c => c == '/') == 1 ? name
                : null;
            if (target is null)
            {
                continue;
            }

            var path = Path.Combine(folder, target.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                entry.DataStream.CopyTo(output);
            }

            hasLibrary |= name == library;
            written.Add(target);
        }

        if (!hasLibrary)
        {
            throw new InvalidDataException("The PDFium archive has no " + library + ".");
        }

        return written;
    }
}
