using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Tools;

/// <summary>
/// Installs Node, ffmpeg and the speech libraries (sherpa-onnx) into <c>&lt;data&gt;/tools/&lt;tool&gt;/&lt;version&gt;/</c> after the user agreed: download → SHA-256 → unpack into a
/// temporary folder → move into place → write a marker. A tool counts as ready only with its marker and executable. On Linux a program on
/// the system PATH may be used when the app has no copy. For <see cref="ToolManifest.FallbackToSystem"/> tools (ffmpeg) on every system:
/// when the pinned download is gone or its hash does not match, the copy on the PATH is used (with ffprobe next to it; logged as a
/// warning and remembered with a marker), otherwise the install fails with "unavailable" and the page shows how to install it. A Node.js
/// of at least <see cref="ToolManifest.NodeMinimumMajor"/> found on the PATH (<see cref="UseSystemNode"/>) is used instead of a download.
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

    private volatile string? _systemNode;

    /// <summary>The pinned archive of a tool on this computer.</summary>
    public ToolArchive? Archive(ToolKind tool) => Archives.FirstOrDefault(a => a.Tool == tool && a.Rid == platform.Rid);

    /// <summary>Uses a suitable Node.js of the system (checked by the caller: version and npm); null forgets it.</summary>
    public void UseSystemNode(string? executable) => _systemNode = executable;

    /// <summary>A program on the PATH (<see cref="SearchPath"/>), or null.</summary>
    public string? FindProgram(string name) => FindOnPath(platform.Executable(name));

    /// <summary>The folder a tool's pinned version is installed into.</summary>
    public string InstallFolder(ToolKind tool) => Archive(tool) is { } archive ? Folder(archive) : Path.Combine(paths.ToolsDirectory, Name(tool));

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

        if (tool == ToolKind.Node && _systemNode is { } node && File.Exists(node))
        {
            return new ToolStatus(tool, true, string.Empty, node, "system");
        }

        if (ToolManifest.Libraries.Contains(tool))
        {
            // A library is used only from the app's own pinned copy.
            return new ToolStatus(tool, false, Archive(tool)?.Version ?? string.Empty, string.Empty, string.Empty);
        }

        // Node.js from the system only after its version was checked (UseSystemNode): an old distribution Node cannot run HyperFrames.
        if (tool != ToolKind.Node && platform.UseSystemPrograms && FindOnPath(platform.Executable(Name(tool))) is { } system)
        {
            return new ToolStatus(tool, true, string.Empty, system, "system");
        }

        if (File.Exists(FallbackFile(tool)) && SystemCopy(tool) is { } fallback)
        {
            return new ToolStatus(tool, true, string.Empty, fallback, FallbackSource);
        }

        return new ToolStatus(tool, false, Archive(tool)?.Version ?? string.Empty, string.Empty, string.Empty);
    }

    /// <summary>
    /// Installs a tool (only after the user agreed; <paramref name="agreed"/> must be true). One install per tool at a time: two rows on the
    /// Tools page can need the same tool (the sherpa-onnx row and a local voice), and two installs would share the download and unpack
    /// paths. A second request waits for the first and returns at once when the first one installed the same version.
    /// </summary>
    public async Task<ToolStatus> InstallAsync(ToolKind tool, bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        if (!agreed)
        {
            throw new InvalidOperationException("A tool is installed only after the user agreed.");
        }

        var archive = Archive(tool) ?? throw new ToolInstallException($"There is no {tool} download for {platform.Rid}.") { Code = "missing" };
        var gate = _installGates.GetOrAdd(tool, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Status(tool) is { Ready: true, Source: "app" } ready && ready.Version == archive.Version)
            {
                logger.LogInformation("{Tool} {Version} was installed by another request meanwhile", tool, archive.Version);
                progress?.Report(new ToolProgress(1, "extract"));
                return ready;
            }

            return await InstallLockedAsync(tool, archive, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<ToolKind, SemaphoreSlim> _installGates = new();

    private async Task<ToolStatus> InstallLockedAsync(ToolKind tool, ToolArchive archive, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
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
            await File.WriteAllTextAsync(Path.Combine(folder, Marker), new JsonObject
            {
                ["version"] = archive.Version,
                ["sha256"] = archive.Sha256,
                ["at"] = DateTimeOffset.UtcNow.ToString("O"),
            }.ToJsonString(), cancellationToken).ConfigureAwait(false);
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
        : throw new ToolInstallException($"{tool} is not installed. It is installed when a process that needs it starts (after a confirmation).") { Code = "missing" };

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
        ToolKind.ImageRuntime => "sd-cpp",
        ToolKind.ImageRuntimeCuda => "sd-cpp-cuda",
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
