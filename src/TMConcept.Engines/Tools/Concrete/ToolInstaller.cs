using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Tools;

/// <summary>
/// Installs Node and ffmpeg into <c>&lt;data&gt;/tools/&lt;tool&gt;/&lt;version&gt;/</c> after the user agreed: download → SHA-256 → unpack into a
/// temporary folder → move into place → write a marker. A tool counts as ready only with its marker and executable. On Linux a program on
/// the system PATH may be used when the app has no copy.
/// </summary>
public sealed class ToolInstaller(IAppPaths paths, IToolPlatform platform, ToolDownloader downloader, ILogger<ToolInstaller> logger)
{
    private const string Marker = ".installed";

    /// <summary>Pinned archives (tests give their own).</summary>
    public IReadOnlyList<ToolArchive> Archives { get; init; } = ToolManifest.Archives;

    /// <summary>The pinned archive of a tool on this computer.</summary>
    public ToolArchive? Archive(ToolKind tool) => Archives.FirstOrDefault(a => a.Tool == tool && a.Rid == platform.Rid);

    /// <summary>Whether a tool is ready and where.</summary>
    public ToolStatus Status(ToolKind tool)
    {
        if (Archive(tool) is { } archive)
        {
            var exe = Executable(archive);
            if (File.Exists(exe) && File.Exists(Path.Combine(Folder(archive), Marker)))
            {
                return new ToolStatus(tool, true, archive.Version, exe, "app");
            }
        }

        if (platform.UseSystemPrograms && FindOnPath(platform.Executable(Name(tool))) is { } system)
        {
            return new ToolStatus(tool, true, string.Empty, system, "system");
        }

        return new ToolStatus(tool, false, Archive(tool)?.Version ?? string.Empty, string.Empty, string.Empty);
    }

    /// <summary>Installs a tool (only after the user agreed; <paramref name="agreed"/> must be true).</summary>
    public async Task<ToolStatus> InstallAsync(ToolKind tool, bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        if (!agreed)
        {
            throw new InvalidOperationException("A tool is installed only after the user agreed.");
        }

        var archive = Archive(tool) ?? throw new ToolInstallException($"There is no {tool} download for {platform.Rid}.") { Code = "missing" };
        var folder = Folder(archive);
        var download = Path.Combine(paths.ToolsDirectory, "downloads");
        var unpack = folder + ".unpack";
        Directory.CreateDirectory(download);
        var file = Path.Combine(download, archive.Url.Segments[^1]);
        try
        {
            await downloader.DownloadAsync(archive, file, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new ToolProgress(1, "extract"));
            Delete(unpack);
            Directory.CreateDirectory(unpack);
            try
            {
                await platform.ExtractAsync(file, unpack, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
            {
                throw new ToolInstallException("The archive could not be unpacked: " + ex.Message, ex) { Code = "extract" };
            }

            if (!File.Exists(Path.Combine(unpack, archive.Root, archive.ExecutablePath)))
            {
                throw new ToolInstallException($"The archive has no {archive.ExecutablePath}.") { Code = "extract" };
            }

            Delete(folder);
            Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
            Directory.Move(unpack, folder);
            await File.WriteAllTextAsync(Path.Combine(folder, Marker), JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["version"] = archive.Version,
                ["sha256"] = archive.Sha256,
                ["at"] = DateTimeOffset.UtcNow.ToString("O"),
            }), cancellationToken).ConfigureAwait(false);
            logger.LogInformation("{Tool} {Version} installed", tool, archive.Version);
            return Status(tool);
        }
        finally
        {
            Delete(unpack);
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>The executable of a tool, or an exception that tells the user to install it.</summary>
    public string Require(ToolKind tool) => Status(tool) is { Ready: true } status
        ? status.ExecutablePath
        : throw new ToolInstallException($"{tool} is not installed. Install it in Settings → Tools.") { Code = "missing" };

    /// <summary>A sibling program of a tool (ffprobe next to ffmpeg, npm-cli next to node).</summary>
    public string Sibling(ToolKind tool, string name)
    {
        var exe = Require(tool);
        return Path.Combine(Path.GetDirectoryName(exe)!, platform.Executable(name));
    }

    private static string Name(ToolKind tool) => tool switch
    {
        ToolKind.Node => "node",
        _ => "ffmpeg",
    };

    private string Folder(ToolArchive archive) => Path.Combine(paths.ToolsDirectory, Name(archive.Tool), archive.Version);

    private string Executable(ToolArchive archive) => Path.Combine(Folder(archive), archive.Root, archive.ExecutablePath);

    private static string? FindOnPath(string program)
    {
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder, program);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void Delete(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
