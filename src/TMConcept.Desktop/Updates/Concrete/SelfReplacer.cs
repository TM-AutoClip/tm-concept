using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// Puts a downloaded, verified new version in the place of the running program. A running program file cannot be overwritten on Windows but
/// can be renamed: the running file becomes <c>&lt;name&gt;.old</c> (removed at the next start), the new one takes its name; if that second
/// step fails the old name comes back. On Linux a rename over the running file is allowed (the running process keeps the old file open), and
/// the other files of the release folder (icon, notices, licences) are replaced too. The new version starts from the same path, so
/// shortcuts and the desktop entry keep working. Only the published single-file program replaces itself (a build from source does not).
/// </summary>
public sealed class SelfReplacer(ILogger<SelfReplacer> logger)
{
    /// <summary>The folder name inside a Linux release archive.</summary>
    public const string ArchiveFolder = "tm-concept";

    /// <summary>The program inside a Linux release archive's folder.</summary>
    public const string LinuxProgram = "tm-concept";

    /// <summary>The running program file.</summary>
    public string ProgramPath { get; init; } = Environment.ProcessPath ?? string.Empty;

    /// <summary>
    /// Whether this is the published single-file program: not run by the dotnet host, and no separate TMConcept.dll next to it (a build from
    /// source has one; the published program carries everything inside).
    /// </summary>
    public bool IsPackaged
    {
        get
        {
            if (ProgramPath.Length == 0 || Path.GetFileNameWithoutExtension(ProgramPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !File.Exists(Path.Combine(Path.GetDirectoryName(ProgramPath)!, "TMConcept.dll"));
        }
    }

    /// <summary>The folder where a new version is downloaded and unpacked (next to the program: the same drive, so the swap is a rename).</summary>
    public string Staging => Path.Combine(Path.GetDirectoryName(ProgramPath)!, UpdateRules.StagingFolder);

    /// <summary>Whether the program's folder can be written (Program Files or a read-only mount cannot).</summary>
    public bool CanWrite()
    {
        try
        {
            Directory.CreateDirectory(Staging);
            var probe = Path.Combine(Staging, "write-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The program's folder cannot be written; the update is not installed by the app");
            return false;
        }
    }

    /// <summary>
    /// The new program file from a downloaded release file: the file itself (Windows), or the program unpacked from the archive (Linux).
    /// Throws <see cref="InvalidDataException"/> for an archive without the program or with a path that leaves its folder.
    /// </summary>
    public string Unpack(string downloaded)
    {
        ArgumentException.ThrowIfNullOrEmpty(downloaded);
        if (!downloaded.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            return downloaded;
        }

        var target = Path.Combine(Staging, "unpacked");
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        Directory.CreateDirectory(target);
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        using (var file = File.OpenRead(downloaded))
        using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        using (var tar = new TarReader(gzip))
        {
            while (tar.GetNextEntry() is { } entry)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
                {
                    continue;
                }

                var path = Path.GetFullPath(Path.Combine(target, entry.Name));
                if (!path.StartsWith(root, StringComparison.Ordinal) || !entry.Name.StartsWith(ArchiveFolder + "/", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The release archive has a file outside its folder: " + entry.Name);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var output = File.Create(path))
                {
                    entry.DataStream.CopyTo(output);
                }

                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(path, entry.Mode);
                }
            }
        }

        var program = Path.Combine(target, ArchiveFolder, LinuxProgram);
        return File.Exists(program) ? program : throw new InvalidDataException("The release archive has no " + ArchiveFolder + "/" + LinuxProgram + ".");
    }

    /// <summary>
    /// Puts <paramref name="newProgram"/> in the place of the running program (see the class). The files next to an unpacked program are
    /// copied next to the running one first. On failure the running program keeps its name and the exception goes up.
    /// </summary>
    public void Replace(string newProgram)
    {
        ArgumentException.ThrowIfNullOrEmpty(newProgram);
        var folder = Path.GetDirectoryName(ProgramPath)!;
        var unpacked = Path.GetDirectoryName(newProgram)!;
        if (!string.Equals(Path.GetFullPath(unpacked), Path.GetFullPath(Staging), StringComparison.Ordinal)
            && Path.GetFullPath(unpacked).StartsWith(Path.GetFullPath(Staging), StringComparison.Ordinal))
        {
            // A Linux release folder: its other files (icon, desktop entry script, README, notices, licences) replace the old ones.
            foreach (var file in Directory.EnumerateFiles(unpacked, "*", SearchOption.AllDirectories)
                .Where(f => !string.Equals(f, newProgram, StringComparison.Ordinal)))
            {
                var target = Path.Combine(folder, Path.GetRelativePath(unpacked, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var old = ProgramPath + UpdateRules.OldSuffix;
            if (File.Exists(old))
            {
                File.Delete(old);
            }

            File.Move(ProgramPath, old);
            try
            {
                File.Move(newProgram, ProgramPath);
            }
            catch
            {
                File.Move(old, ProgramPath);
                throw;
            }
        }
        else
        {
            File.SetUnixFileMode(newProgram, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.Move(newProgram, ProgramPath, overwrite: true);
        }

        logger.LogInformation("The program file was replaced by the new version");
    }

    /// <summary>At start: removes what an update left (the old program file on Windows, the download folder). Failures are only logged.</summary>
    public void CleanUp()
    {
        if (ProgramPath.Length == 0)
        {
            return;
        }

        try
        {
            var old = ProgramPath + UpdateRules.OldSuffix;
            if (File.Exists(old))
            {
                File.Delete(old);
                logger.LogInformation("The program file of the previous version was removed");
            }

            if (Directory.Exists(Staging))
            {
                Directory.Delete(Staging, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "What the last update left could not be removed; it is tried again at the next start");
        }
    }
}
