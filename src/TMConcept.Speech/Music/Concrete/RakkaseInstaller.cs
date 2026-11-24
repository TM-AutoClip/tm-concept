using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Music;

/// <summary>
/// Installs the local music model (0.1.46, C5) the way rakkase's own <c>scripts/kur.ps1</c> does, with the app's uv and Python (called by the
/// components only after the user agreed to the download list). Environment: rakkase's and ACE-Step's source archives (pinned by SHA-256,
/// <see cref="RakkasePins"/>) unpacked into their folders (the weights and the environment stay where they are), <c>uv sync --frozen</c> in
/// ACE-Step's folder with the app's pinned Python (exactly ACE-Step's <c>uv.lock</c>, PyTorch for CUDA 12.8 included), rakkase's
/// <c>ayarlar.json</c> (<c>ace_kok</c> = ACE-Step's folder, no outside lock), then the marker. Weights: every file of rakkase's pinned
/// <c>scripts/modeller.json</c> into <c>checkpoints/</c>, each pinned by its SHA-256 and size, only from Hugging Face, never outside that folder;
/// the marker last. Nothing runs at install that the user did not agree to; the model itself is never started here.
/// </summary>
public sealed class RakkaseInstaller(RakkaseLayout layout, PythonInstaller python, PythonLayout pythonLayout, IToolLocator tools, IProcessRunner runner,
    IArchiveDownloader downloader, ILogger<RakkaseInstaller> logger)
{
    private IReadOnlyList<RakkaseWeight>? _weights;

    /// <summary>The fingerprint of the environment: both commits (a new pin installs it again).</summary>
    public static string EnvironmentFingerprint => RakkasePins.RakkaseCommit + "|" + RakkasePins.AceCommit;

    /// <summary>Whether the environment is installed for these pins (files only).</summary>
    public bool EnvironmentReady
    {
        get
        {
            try
            {
                return File.Exists(layout.Python) && File.Exists(layout.Script) && File.Exists(layout.Settings) && File.Exists(layout.EnvironmentMarker)
                    && File.ReadAllText(layout.EnvironmentMarker).Trim() == EnvironmentFingerprint;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>Whether every weight file is there with its size and the marker names this revision (files only).</summary>
    public bool WeightsReady
    {
        get
        {
            try
            {
                return File.Exists(layout.WeightsMarker) && File.ReadAllText(layout.WeightsMarker).Trim() == RakkasePins.WeightsRevision
                    && Weights() is { Count: > 0 } weights && weights.All(Present);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                return false;
            }
        }
    }

    /// <summary>The <c>uv sync</c> arguments: exactly ACE-Step's lock, with the app's Python, never a Python downloaded by uv.</summary>
    public static IReadOnlyList<string> SyncArguments(string interpreter) => ["sync", "--frozen", "--python", interpreter, "--no-python-downloads"];

    /// <summary>
    /// The variables of the <c>uv sync</c> run: the app's Python and cache folders, no progress bars, no launcher or registry entry, the
    /// user's own environment emptied. ACE-Step's <c>pyproject.toml</c> is read (its PyTorch index is in it), so uv's settings files are not
    /// switched off here as they are for the voices.
    /// </summary>
    public IReadOnlyDictionary<string, string> SyncEnvironment() => new Dictionary<string, string>
    {
        ["UV_PYTHON_INSTALL_DIR"] = pythonLayout.PythonFolder,
        ["UV_CACHE_DIR"] = pythonLayout.CacheFolder,
        ["UV_NO_PROGRESS"] = "1",
        ["UV_PYTHON_INSTALL_BIN"] = "0",
        ["UV_PYTHON_INSTALL_REGISTRY"] = "0",
        ["UV_PROJECT_ENVIRONMENT"] = string.Empty,
        ["VIRTUAL_ENV"] = string.Empty,
        ["PYTHONPATH"] = string.Empty,
        ["PYTHONHOME"] = string.Empty,
    };

    /// <summary>rakkase's settings, as its <c>kur.ps1</c> writes them (forward slashes, no outside GPU lock: the app holds the card's lock).</summary>
    public static string SettingsJson(string aceFolder) => new JsonObject
    {
        ["ace_kok"] = aceFolder.Replace('\\', '/'),
        ["kilit"] = false,
        ["kilit_src"] = string.Empty,
        ["kilit_bekle_sn"] = 3600,
        ["on_plan_ses_siniri_sn"] = 300,
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Installs the environment (network; only after the user agreed).</summary>
    public async Task InstallEnvironmentAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var uv = tools.Status(ToolKind.Uv) is { Ready: true } status
            ? status.ExecutablePath
            : throw new ToolInstallException("uv is not installed. It is installed with the Python components (after a confirmation).") { Code = "missing" };
        var interpreter = python.Interpreter
            ?? throw new ToolInstallException("The Python of the local models is not installed.") { Code = "missing" };
        Directory.CreateDirectory(layout.Root);
        progress?.Report(new ToolProgress(0.01, "rakkase"));
        await UnpackAsync(RakkasePins.Rakkase, layout.RakkaseFolder, cancellationToken).ConfigureAwait(false);
        progress?.Report(new ToolProgress(0.03, "ACE-Step 1.5"));
        await UnpackAsync(RakkasePins.Ace, layout.AceFolder, cancellationToken).ConfigureAwait(false);

        progress?.Report(new ToolProgress(0.05, "uv sync"));
        Directory.CreateDirectory(layout.Home);
        logger.LogInformation("uv {Arguments} (ACE-Step 1.5)", string.Join(' ', SyncArguments(Path.GetFileName(interpreter))));
        var result = await runner.RunAsync(new ProcessStart(uv, SyncArguments(interpreter), layout.AceFolder) { Environment = SyncEnvironment() }, line =>
        {
            if (line.Length > 0)
            {
                progress?.Report(new ToolProgress(0.5, line.Length > 120 ? line[..120] : line));
            }
        }, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(layout.Python))
        {
            logger.LogError("uv sync of ACE-Step failed (exit code {Code}): {Output}", result.ExitCode, ChildOutput.Summary(result.LastLines));
            throw new ToolInstallException("uv failed: " + ChildOutput.Summary(result.LastLines)) { Code = "install" };
        }

        await File.WriteAllTextAsync(layout.Settings, SettingsJson(layout.AceFolder), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(layout.EnvironmentMarker, EnvironmentFingerprint, cancellationToken).ConfigureAwait(false);
        TryDelete(layout.Downloads);
        progress?.Report(new ToolProgress(1, "ACE-Step 1.5"));
        logger.LogInformation("The local music model's environment is installed (rakkase {Rakkase}, ACE-Step {Ace})", RakkasePins.RakkaseCommit[..12],
            RakkasePins.AceCommit[..12]);
    }

    /// <summary>The weights rakkase lists (read from its pinned source once; empty while it is not installed).</summary>
    public IReadOnlyList<RakkaseWeight> Weights()
    {
        if (_weights is not null)
        {
            return _weights;
        }

        if (!File.Exists(layout.WeightList))
        {
            return [];
        }

        return _weights = ReadWeights(File.ReadAllText(layout.WeightList), layout.Checkpoints);
    }

    /// <summary>
    /// The weights of rakkase's <c>modeller.json</c> (<c>dosyalar</c>: <c>hedef</c>, <c>url</c>, <c>sha256</c>, <c>boyut</c>): only https
    /// addresses of Hugging Face, a 64-digit SHA-256, a size, and a target inside <paramref name="checkpoints"/>; anything else makes the list
    /// unusable (<see cref="InvalidDataException"/>).
    /// </summary>
    public static IReadOnlyList<RakkaseWeight> ReadWeights(string json, string checkpoints)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrEmpty(checkpoints);
        var root = Path.GetFullPath(checkpoints).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var list = new List<RakkaseWeight>();
        foreach (var item in JsonNode.Parse(json)?["dosyalar"] as JsonArray ?? throw new InvalidDataException("The weight list has no files."))
        {
            var target = item?["hedef"]?.GetValue<string>() ?? string.Empty;
            var address = item?["url"]?.GetValue<string>() ?? string.Empty;
            var sha = item?["sha256"]?.GetValue<string>() ?? string.Empty;
            var size = item?["boyut"]?.GetValue<long>() ?? 0;
            var full = Path.GetFullPath(Path.Combine(root, target));
            if (target.Length == 0 || Path.IsPathRooted(target) || !full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                || !Uri.TryCreate(address, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps || url.Host != RakkasePins.WeightsHost
                || sha.Length != 64 || !sha.All(Uri.IsHexDigit) || size <= 0)
            {
                throw new InvalidDataException("The weight list has an entry the app does not accept: " + target);
            }

            list.Add(new RakkaseWeight(target, full, url, sha.ToLowerInvariant(), size));
        }

        return list.Count > 0 ? list : throw new InvalidDataException("The weight list has no files.");
    }

    /// <summary>Downloads every missing weight file (network; only after the user agreed), then writes the marker.</summary>
    public async Task InstallWeightsAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        _weights = null;
        var weights = Weights();
        if (weights.Count == 0)
        {
            throw new ToolInstallException("The local music model's environment is not installed.") { Code = "missing" };
        }

        var missing = weights.Where(w => !Present(w)).ToList();
        var total = Math.Max(1, missing.Sum(w => w.Bytes));
        long done = 0;
        foreach (var weight in missing)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(weight.File)!);
            var before = done;
            var part = new Progress<ToolProgress>(p => progress?.Report(new ToolProgress((before + (p.Share * weight.Bytes)) / total, weight.Target)));
            await downloader.DownloadAsync(new ToolArchive(ToolKind.MusicWeights, RakkasePins.WeightsRevision[..12], "any", weight.Url, weight.Sha256,
                string.Empty, weight.Target, weight.Bytes), weight.File, part, cancellationToken).ConfigureAwait(false);
            done += weight.Bytes;
        }

        await File.WriteAllTextAsync(layout.WeightsMarker, RakkasePins.WeightsRevision, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("The local music model's weights are installed ({Count} files, {Downloaded} downloaded)", weights.Count, missing.Count);
    }

    private static bool Present(RakkaseWeight weight)
    {
        var file = new FileInfo(weight.File);
        return file.Exists && file.Length == weight.Bytes;
    }

    /// <summary>Downloads a source archive and copies its tree into <paramref name="target"/> (files there are replaced, others kept).</summary>
    private async Task UnpackAsync(ToolArchive archive, string target, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(layout.Downloads);
        var zip = Path.Combine(layout.Downloads, Path.GetFileName(archive.Root) + ".zip");
        await downloader.DownloadAsync(archive, zip, null, cancellationToken).ConfigureAwait(false);
        var staging = Path.Combine(layout.Downloads, "unpacked-" + Path.GetFileName(target));
        TryDelete(staging);
        await Task.Run(() =>
        {
            ZipFile.ExtractToDirectory(zip, staging, overwriteFiles: true);
            var top = Path.Combine(staging, archive.Root);
            if (!File.Exists(Path.Combine(top, archive.ExecutablePath)))
            {
                throw new ToolInstallException($"The archive of {archive.Root} has no {archive.ExecutablePath}.") { Code = "archive" };
            }

            CopyTree(top, target);
        }, cancellationToken).ConfigureAwait(false);
        TryDelete(staging);
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var folder in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, folder)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
        }
    }

    private void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "A temporary folder of the music model's install could not be removed");
        }
    }
}
