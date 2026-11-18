using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// Puts a downloaded, verified new version in the place of the running program. A running program file cannot be overwritten on Windows but
/// can be renamed: the running file becomes <c>&lt;name&gt;.old</c> (removed once the next start has opened its main window), the new one takes its name; if that second
/// step fails the old name comes back. On Linux a rename over the running file is allowed (the running process keeps the old file open), and
/// the other files of the release folder (icon, notices, licences) are replaced too. The new version starts from the same path, so
/// shortcuts and the desktop entry keep working. Only the published single-file program replaces itself (a build from source does not).
/// <para>0.1.26: a program file named by its release (<c>TMConcept-0.1.17-win-x64.exe</c>, as 0.1.25 and older were published) becomes
/// <see cref="ReleaseAssetNames.PortableWindows"/> in the same folder (when that name is free): a name with a version would stay wrong after
/// every update. A shortcut the user made to the old name stops working then; the release notes say so.</para>
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
    /// Where the new version goes: the running program's own path, or <see cref="ReleaseAssetNames.PortableWindows"/> next to it when the
    /// running file is named by its release and that name is free.
    /// </summary>
    public string TargetPath
    {
        get
        {
            var name = Path.GetFileName(ProgramPath);
            if (!ReleaseAssetNames.VersionedWindowsProgram.IsMatch(name))
            {
                return ProgramPath;
            }

            var target = Path.Combine(Path.GetDirectoryName(ProgramPath)!, ReleaseAssetNames.PortableWindows);
            if (File.Exists(target))
            {
                logger.LogWarning("{Name} already exists next to the program; the new version keeps the name {Old}", ReleaseAssetNames.PortableWindows, name);
                return ProgramPath;
            }

            return target;
        }
    }

    /// <summary>
    /// Puts <paramref name="newProgram"/> in the place of the running program (see the class) and returns the path it is at now
    /// (<see cref="TargetPath"/>). The files next to an unpacked program are copied next to the running one first. On failure the running
    /// program keeps its name and the exception goes up.
    /// </summary>
    public string Replace(string newProgram)
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
                var copy = Path.Combine(folder, Path.GetRelativePath(unpacked, file));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(file, copy, overwrite: true);
            }
        }

        var target = TargetPath;
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
                File.Move(newProgram, target);
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
            File.Move(newProgram, target, overwrite: true);
            if (!string.Equals(target, ProgramPath, StringComparison.Ordinal))
            {
                File.Delete(ProgramPath);
            }
        }

        if (string.Equals(target, ProgramPath, StringComparison.Ordinal))
        {
            logger.LogInformation("The program file was replaced by the new version");
        }
        else
        {
            logger.LogInformation("The program file was replaced by the new version, now named {Name} (was {Old})", Path.GetFileName(target),
                Path.GetFileName(ProgramPath));
        }

        return target;
    }

    /// <summary>
    /// The previous version's program file an update left (Windows: <c>&lt;name&gt;.old</c>); null when there is none. It is kept until the new
    /// version has opened the database and shown its main window, so a failed database upgrade can still go back to it.
    /// </summary>
    public string? PreviousProgram
    {
        get
        {
            if (ProgramPath.Length == 0)
            {
                return null;
            }

            return File.Exists(ProgramPath + UpdateRules.OldSuffix) ? ProgramPath + UpdateRules.OldSuffix : RenamedPrograms().FirstOrDefault();
        }
    }

    /// <summary>
    /// After the main window opened (the database is unlocked and upgraded): removes what an update left (the old program file on Windows, the
    /// download folder). Failures are only logged.
    /// </summary>
    public void CleanUp()
    {
        if (ProgramPath.Length == 0)
        {
            return;
        }

        try
        {
            foreach (var old in new[] { ProgramPath + UpdateRules.OldSuffix }.Where(File.Exists).Concat(RenamedPrograms()).ToList())
            {
                File.Delete(old);
                logger.LogInformation("The program file of the previous version was removed ({Name})", Path.GetFileName(old));
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

    /// <summary>
    /// The previous program files an update left under their old release name (<c>TMConcept-0.1.17-win-x64.exe.old</c> next to a
    /// <see cref="ReleaseAssetNames.PortableWindows"/> that was renamed from it), newest first; none for any other program name.
    /// </summary>
    private List<string> RenamedPrograms()
    {
        var folder = Path.GetDirectoryName(ProgramPath);
        if (folder is null || !string.Equals(Path.GetFileName(ProgramPath), ReleaseAssetNames.PortableWindows, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(folder, "TMConcept-*-win-x64.exe" + UpdateRules.OldSuffix)
            .Where(f => ReleaseAssetNames.VersionedWindowsProgram.IsMatch(Path.GetFileName(f)[..^UpdateRules.OldSuffix.Length]))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }
}
