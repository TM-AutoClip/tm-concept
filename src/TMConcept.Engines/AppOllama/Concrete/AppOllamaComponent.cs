using System.IO.Compression;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Components;
using TMConcept.Core.Hardware;
using TMConcept.Core.Paths;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.AppOllama;

/// <summary>
/// "Ollama (TM Concept)": the app's own copy of Ollama in <c>&lt;tools&gt;/ollama/&lt;version&gt;</c> (0.1.20). Installed only after the user
/// confirmed the download (about 1.6 GB on Windows): the official release package (<see cref="AppOllamaRelease"/>) through the resuming
/// downloader, its SHA-256 checked against the release digest before anything is unpacked, unpacked into a temporary folder (zip, or
/// <c>.tar.zst</c> with <see cref="ZstdTarExtractor"/> on Linux), the graphics backends this computer does not need deleted
/// (<see cref="AppOllamaBackends"/>; never written while unpacking), then moved into place with a marker that names them. A backend the
/// computer needs later (a card or a driver added) makes the copy not ready, so it is offered again. A failed unpack keeps the verified
/// package for the next try; a full disk says so ("disk"). A new pinned version installs into a new folder (the old one
/// shows as a leftover in the storage manager). Its folders are the program and the server's own home folder; the models are components
/// of their own. It never touches an Ollama the user installed.
/// </summary>
public sealed class AppOllamaComponent(IAppPaths paths, IToolPlatform platform, IArchiveDownloader downloader, IGpuInfo gpu, AppOllamaLayout layout,
    ILogger<AppOllamaComponent> logger) : IComponent, IAppOllamaRuntime
{
    private const string Marker = ".installed";
    private const string RemovedKey = "removed";
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
    /// <remarks>Also not ready when a graphics backend this computer needs now was left out at the install (<see cref="MissingBackends"/>).</remarks>
    public bool Ready => Archive is not null && File.Exists(Executable) && File.Exists(Path.Combine(InstallFolder, Marker)) && MissingBackends().Count == 0;

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
        var installed = false;
        try
        {
            var download = new Progress<ToolProgress>(p => progress?.Report(new ToolProgress(p.Share * 0.85, p.Text)));
            await downloader.DownloadAsync(archive, file, download, cancellationToken).ConfigureAwait(false);
            progress?.Report(new ToolProgress(0.9, "extract"));
            Delete(unpack);
            Directory.CreateDirectory(unpack);

            // The backends this computer does not need are never written (about 2 GB less on the disk while unpacking).
            var report = gpu.Detect();
            var keep = AppOllamaBackends.Keep(report);
            var skipped = new SortedSet<string>(StringComparer.Ordinal);
            bool Skip(string name)
            {
                if (AppOllamaBackends.BackendOf(name) is not { } backend || keep.Contains(backend, StringComparer.Ordinal))
                {
                    return false;
                }

                skipped.Add(backend);
                return true;
            }

            try
            {
                await ExtractAsync(file, unpack, cancellationToken, Skip).ConfigureAwait(false);
            }
            catch (IOException ex) when (ToolDownloader.DiskFull(ex))
            {
                logger.LogError(ex, "{Name} {Version}: the disk is full while unpacking; the verified package is kept", Name, archive.Version);
                throw new ToolInstallException("The disk is full.", ex) { Code = "disk", Detail = ex.Message.Trim() };
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
                or ZstdSharp.ZstdException)
            {
                throw new ToolInstallException("The Ollama package could not be unpacked: " + ex.Message, ex) { Code = "extract", Detail = ex.Message };
            }

            var executable = Path.Combine(unpack, archive.Root, archive.ExecutablePath);
            if (!File.Exists(executable))
            {
                throw new ToolInstallException($"The Ollama package has no {archive.ExecutablePath}.") { Code = "extract" };
            }

            var removed = skipped.Concat(Prune(Path.Combine(unpack, archive.Root, "lib", "ollama"), report)).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToList();
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
                ["backends"] = new JsonArray(keep.Select(b => (JsonNode)JsonValue.Create(b)).ToArray()),
                [RemovedKey] = new JsonArray(removed.Select(b => (JsonNode)JsonValue.Create(b)).ToArray()),
                ["at"] = DateTimeOffset.UtcNow.ToString("O"),
            }.ToJsonString(), cancellationToken).ConfigureAwait(false);
            installed = true;
            logger.LogInformation("{Name} {Version} installed; graphics backends kept: {Kept}; left out: {Removed}", Name, archive.Version,
                keep.Count > 0 ? string.Join(", ", keep) : "none (processor)", removed.Count > 0 ? string.Join(", ", removed) : "none");
            progress?.Report(new ToolProgress(1, "extract"));
        }
        finally
        {
            Delete(unpack);
            Release(file, installed, archive);
        }
    }

    /// <summary>
    /// After an install: the package is deleted when it succeeded. Otherwise a verified package (an unpack that failed: a full disk, a locked
    /// file) is kept as the download's part, so the next try only checks it again instead of downloading 1.6 GB from the start.
    /// </summary>
    private void Release(string file, bool installed, ToolArchive archive)
    {
        if (!File.Exists(file))
        {
            return;
        }

        try
        {
            if (installed)
            {
                File.Delete(file);
            }
            else
            {
                File.Move(file, file + ".part", overwrite: true);
                logger.LogInformation("{Name} {Version}: the verified package is kept for the next try", Name, archive.Version);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The install's own outcome is what the user sees; the package stays a leftover in the storage manager.
            logger.LogWarning(ex, "{Name} {Version}: the downloaded package could not be {What}", Name, archive.Version, installed ? "deleted" : "kept");
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

    /// <summary>
    /// Unpacks a zip with .NET or a <c>.tar.zst</c> with the managed zstd decoder, entry by entry: an entry <paramref name="skip"/> accepts
    /// (its name inside the archive, with <c>/</c>) is not written, and every entry must land inside <paramref name="targetFolder"/>.
    /// </summary>
    public static Task ExtractAsync(string archive, string targetFolder, CancellationToken cancellationToken, Func<string, bool>? skip = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(targetFolder);
        if (archive.EndsWith(".tar.zst", StringComparison.OrdinalIgnoreCase))
        {
            return ZstdTarExtractor.ExtractAsync(archive, targetFolder, cancellationToken, skip);
        }

        return Task.Run(() => ExtractZip(archive, targetFolder, skip, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// The graphics backends this computer needs now that the installed copy lacks: left out at the install (the card or its driver was not
    /// seen then, or a card was added later). The copy then is not ready, so the confirm flow offers to install it again. A marker that cannot
    /// be read names nothing (the copy was installed; it is used as it is).
    /// </summary>
    public IReadOnlyList<string> MissingBackends()
    {
        var marker = Path.Combine(InstallFolder, Marker);
        if (!File.Exists(marker))
        {
            return [];
        }

        JsonArray? removed;
        try
        {
            removed = JsonNode.Parse(File.ReadAllText(marker))?[RemovedKey] as JsonArray;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            logger.LogWarning(ex, "{Name}: its install marker could not be read; its graphics backends are not checked", Name);
            return [];
        }

        var left = (removed ?? []).Select(n => n?.GetValueKind() == System.Text.Json.JsonValueKind.String ? n.GetValue<string>() : null).OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        return AppOllamaBackends.Keep(gpu.Detect()).Where(left.Contains).ToList();
    }

    private static void ExtractZip(string archive, string targetFolder, Func<string, bool>? skip, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetFolder);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetFolder)) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = ZstdTarExtractor.Normalized(entry.FullName);
            var target = ZstdTarExtractor.Inside(root, name);
            if (skip?.Invoke(name) == true)
            {
                continue;
            }

            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
    }

    private static void Delete(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
