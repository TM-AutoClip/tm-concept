using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Tools;

/// <summary>
/// Installs Node, ffmpeg and the speech libraries (sherpa-onnx) into <c>&lt;data&gt;/tools/&lt;tool&gt;/&lt;version&gt;/</c> after the user agreed: download → SHA-256 → unpack into a
/// temporary folder → move into place → write a marker. A tool counts as ready only with its marker and executable. On Linux a program on
/// the system PATH may be used when the app has no copy. For <see cref="ToolManifest.FallbackToSystem"/> tools (ffmpeg) on every system:
/// when the pinned download is gone or its hash does not match, the copy on the PATH is used (with ffprobe next to it; logged as a
/// warning and remembered with a marker), otherwise the install fails with "unavailable" and the page shows how to install it.
/// </summary>
public sealed class ToolInstaller(IAppPaths paths, IToolPlatform platform, ToolDownloader downloader, ILogger<ToolInstaller> logger) : IToolLocator
{
    /// <summary>Source of a tool that came from the PATH because its pinned download was not usable.</summary>
    public const string FallbackSource = "fallback";

    private const string Marker = ".installed";
    private const string FallbackMarker = "use-system";

    /// <summary>Pinned archives (tests give their own).</summary>
    public IReadOnlyList<ToolArchive> Archives { get; init; } = ToolManifest.Archives;

    /// <summary>The folders searched for a system copy (the PATH; tests give their own).</summary>
    public string SearchPath { get; init; } = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

    /// <summary>The pinned archive of a tool on this computer.</summary>
    public ToolArchive? Archive(ToolKind tool) => Archives.FirstOrDefault(a => a.Tool == tool && a.Rid == platform.Rid);

    /// <inheritdoc />
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

        if (ToolManifest.Libraries.Contains(tool))
        {
            // A library is used only from the app's own pinned copy.
            return new ToolStatus(tool, false, Archive(tool)?.Version ?? string.Empty, string.Empty, string.Empty);
        }

        if (platform.UseSystemPrograms && FindOnPath(platform.Executable(Name(tool))) is { } system)
        {
            return new ToolStatus(tool, true, string.Empty, system, "system");
        }

        if (File.Exists(FallbackFile(tool)) && SystemCopy(tool) is { } fallback)
        {
            return new ToolStatus(tool, true, string.Empty, fallback, FallbackSource);
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
            try
            {
                await downloader.DownloadAsync(archive, file, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (ToolInstallException ex) when (ex.Code is "gone" or "hash" && ToolManifest.FallbackToSystem.Contains(tool))
            {
                return await FallBackAsync(tool, ex, cancellationToken).ConfigureAwait(false);
            }

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

    /// <summary>The pinned download is not usable: the system's copy (with its siblings, ffprobe) is used, or the install fails clearly.</summary>
    private async Task<ToolStatus> FallBackAsync(ToolKind tool, ToolInstallException cause, CancellationToken cancellationToken)
    {
        if (SystemCopy(tool) is null)
        {
            logger.LogError("{Tool}: the pinned download is not usable ({Code}) and there is no {Tool} on the system PATH", tool, cause.Code, tool);
            throw new ToolInstallException($"The pinned {tool} download is not usable ({cause.Message}) and there is no {tool} on the PATH.", cause)
            {
                Code = "unavailable",
            };
        }

        // The file name only: a full path would name the user's folders in the log.
        logger.LogWarning("{Tool}: the pinned download is not usable ({Code}); using the copy on the system PATH ({Program})", tool, cause.Code,
            platform.Executable(Name(tool)));
        var marker = FallbackFile(tool);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        await File.WriteAllTextAsync(marker, cause.Code, cancellationToken).ConfigureAwait(false);
        return Status(tool);
    }

    /// <summary>The tool on the PATH when its sibling programs are next to it (ffprobe beside ffmpeg), else null.</summary>
    private string? SystemCopy(ToolKind tool)
    {
        if (FindOnPath(platform.Executable(Name(tool))) is not { } exe)
        {
            return null;
        }

        var folder = Path.GetDirectoryName(exe)!;
        return Siblings(tool).All(s => File.Exists(Path.Combine(folder, platform.Executable(s)))) ? exe : null;
    }

    private static IReadOnlyList<string> Siblings(ToolKind tool) => tool == ToolKind.Ffmpeg ? ["ffprobe"] : [];

    private string FallbackFile(ToolKind tool) => Path.Combine(paths.ToolsDirectory, Name(tool), FallbackMarker);

    private static string Name(ToolKind tool) => tool switch
    {
        ToolKind.Node => "node",
        ToolKind.Ffmpeg => "ffmpeg",
        ToolKind.SpeechRuntime => "sherpa-onnx",
        _ => tool.ToString().ToLowerInvariant(),
    };

    private string Folder(ToolArchive archive) => Path.Combine(paths.ToolsDirectory, Name(archive.Tool), archive.Version);

    private string Executable(ToolArchive archive) => Path.Combine(Folder(archive), archive.Root, archive.ExecutablePath);

    private string? FindOnPath(string program)
    {
        foreach (var folder in SearchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
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
