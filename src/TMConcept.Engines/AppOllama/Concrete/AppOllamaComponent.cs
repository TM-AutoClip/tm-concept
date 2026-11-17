using System.IO.Compression;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Components;
using TMConcept.Core.Hardware;
using TMConcept.Core.Paths;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.AppOllama;

/// <summary>
/// "Ollama (TM Concept)": the app's own copy of Ollama in <c>&lt;tools&gt;/ollama/&lt;version&gt;</c> (0.1.20). Installed only after the user
/// confirmed the download (about 1.6 GB on Windows): the official release package (<see cref="AppOllamaRelease"/>) through the resuming
/// downloader, its SHA-256 checked against the release digest before anything is unpacked, unpacked into a temporary folder (zip, or
/// <c>.tar.zst</c> with <see cref="ZstdTarExtractor"/> on Linux), the graphics backends this computer does not need deleted
/// (<see cref="AppOllamaBackends"/>), then moved into place with a marker. A new pinned version installs into a new folder (the old one
/// shows as a leftover in the storage manager). Its folders are the program and the server's own home folder; the models are components
/// of their own. It never touches an Ollama the user installed.
/// </summary>
public sealed class AppOllamaComponent(IAppPaths paths, IToolPlatform platform, IArchiveDownloader downloader, IGpuInfo gpu, AppOllamaLayout layout,
    ILogger<AppOllamaComponent> logger) : IComponent, IAppOllamaRuntime
{
    private const string Marker = ".installed";
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The pinned packages (tests give their own).</summary>
    public IReadOnlyList<ToolArchive> Archives { get; init; } = AppOllamaRelease.Archives;

    /// <inheritdoc />
    public string Code => ComponentCodes.AppOllama;

    /// <inheritdoc />
    public string Name => AppOllamaDefaults.Name;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public bool Supported => Archive is not null;

    /// <inheritdoc />
    public bool Ready => Archive is not null && File.Exists(Executable) && File.Exists(Path.Combine(InstallFolder, Marker));

    /// <inheritdoc />
    public string Version => Archive?.Version ?? AppOllamaRelease.Version;

    /// <inheritdoc />
    public string Executable => Archive is { } archive ? Path.Combine(InstallFolder, archive.Root, archive.ExecutablePath) : string.Empty;

    /// <summary>The folder of the pinned version (<c>&lt;tools&gt;/ollama/&lt;version&gt;</c>).</summary>
    public string InstallFolder => Path.Combine(Root, Version);

    /// <summary>The component's own folder that holds its versions.</summary>
    public string Root => Path.Combine(paths.ToolsDirectory, "ollama");

    /// <inheritdoc />
    public bool WasInstalled => ComponentFolders.HasContent(Root);

    private ToolArchive? Archive => Archives.FirstOrDefault(a => a.Rid == platform.Rid);

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() =>
        Ready || Archive is not { } archive ? [] : [new ComponentDownload(Name + " " + archive.Version, archive.Url.Host, archive.ApproximateBytes)];

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => new[] { InstallFolder, layout.HomeDirectory }.Where(Directory.Exists).ToList();

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var archive = Archive ?? throw new ToolInstallException($"There is no Ollama package for {platform.Rid}.") { Code = "missing" };
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Ready)
            {
                progress?.Report(new ToolProgress(1, "extract"));
                return;
            }

            await InstallLockedAsync(archive, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task InstallLockedAsync(ToolArchive archive, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var downloads = Path.Combine(paths.ToolsDirectory, "downloads");
        Directory.CreateDirectory(downloads);
        var file = Path.Combine(downloads, archive.Url.Segments[^1]);
        var unpack = InstallFolder + ".unpack";
        try
        {
            var download = new Progress<ToolProgress>(p => progress?.Report(new ToolProgress(p.Share * 0.85, p.Text)));
            await downloader.DownloadAsync(archive, file, download, cancellationToken).ConfigureAwait(false);
            progress?.Report(new ToolProgress(0.9, "extract"));
            Delete(unpack);
            Directory.CreateDirectory(unpack);
            try
            {
                await ExtractAsync(file, unpack, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ZstdSharp.ZstdException)
            {
                throw new ToolInstallException("The Ollama package could not be unpacked: " + ex.Message, ex) { Code = "extract" };
            }

            var executable = Path.Combine(unpack, archive.Root, archive.ExecutablePath);
            if (!File.Exists(executable))
            {
                throw new ToolInstallException($"The Ollama package has no {archive.ExecutablePath}.") { Code = "extract" };
            }

            var report = gpu.Detect();
            var removed = Prune(Path.Combine(unpack, archive.Root, "lib", "ollama"), report);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            progress?.Report(new ToolProgress(0.98, "extract"));
            Delete(InstallFolder);
            Directory.CreateDirectory(Root);
            Directory.Move(unpack, InstallFolder);
            await File.WriteAllTextAsync(Path.Combine(InstallFolder, Marker), new JsonObject
            {
                ["version"] = archive.Version,
                ["sha256"] = archive.Sha256,
                ["backends"] = new JsonArray(AppOllamaBackends.Keep(report).Select(b => (JsonNode)JsonValue.Create(b)).ToArray()),
                ["removed"] = new JsonArray(removed.Select(b => (JsonNode)JsonValue.Create(b)).ToArray()),
                ["at"] = DateTimeOffset.UtcNow.ToString("O"),
            }.ToJsonString(), cancellationToken).ConfigureAwait(false);
            logger.LogInformation("{Name} {Version} installed; graphics backends kept: {Kept}; removed: {Removed}", Name, archive.Version,
                AppOllamaBackends.Keep(report) is { Count: > 0 } kept ? string.Join(", ", kept) : "none (processor)",
                removed.Count > 0 ? string.Join(", ", removed) : "none");
            progress?.Report(new ToolProgress(1, "extract"));
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

    /// <summary>
    /// Deletes the backend folders of an unpacked package that this computer does not need (<see cref="AppOllamaBackends.Unneeded"/>);
    /// returns their names.
    /// </summary>
    public static IReadOnlyList<string> Prune(string libraryFolder, GpuReport report)
    {
        var unneeded = AppOllamaBackends.Unneeded(libraryFolder, report);
        foreach (var folder in unneeded)
        {
            Directory.Delete(folder, recursive: true);
        }

        return unneeded.Select(Path.GetFileName).OfType<string>().ToList();
    }

    /// <summary>Unpacks a zip with .NET or a <c>.tar.zst</c> with the managed zstd decoder.</summary>
    public static Task ExtractAsync(string archive, string targetFolder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.EndsWith(".tar.zst", StringComparison.OrdinalIgnoreCase))
        {
            return ZstdTarExtractor.ExtractAsync(archive, targetFolder, cancellationToken);
        }

        return Task.Run(() => ZipFile.ExtractToDirectory(archive, targetFolder, overwriteFiles: false), cancellationToken);
    }

    private static void Delete(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
