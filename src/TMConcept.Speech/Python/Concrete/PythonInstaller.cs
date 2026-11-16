using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;
using TMConcept.Core.Processes;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Python;

/// <summary>
/// Installs the Python voices' Python and environments with the pinned uv (only after the user confirmed the downloads; called by the
/// components). The Python: <c>uv python install 3.11.18</c> into the tools folder, without a launcher on the PATH and without a Windows
/// registry entry. An environment: <c>uv venv</c> with that Python, then <c>uv pip sync</c> of the shipped lock file: exactly those versions,
/// nothing else, from PyPI and PyTorch's own index only (<c>--torch-backend</c>), nothing newer than the lock's date. PyTorch's build follows
/// the computer (<see cref="Backend"/>): CUDA 12.8 when an NVIDIA card and its driver are there, otherwise the processor build. The
/// environment's marker holds a fingerprint of Python, build and lock; when any of them changes (a new app version, a new card) the
/// environment counts as missing and is installed again. uv's own settings files and package index variables are ignored.
/// </summary>
public sealed class PythonInstaller(PythonLayout layout, IToolLocator tools, IProcessRunner runner, IGpuInfo gpu, ILogger<PythonInstaller> logger)
{
    private static readonly TimeSpan[] LockRetries = [TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10)];
    private static readonly string[] FileLockMarkers = ["os error 5)", "os error 32)", "Access is denied", "being used by another process"];

    /// <summary>The PyTorch build for <paramref name="environment"/> on this computer (<see cref="PythonPins.CudaBackend"/> or <see cref="PythonPins.CpuBackend"/>).</summary>
    public string Backend(PythonEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return Backend(environment, gpu.Detect());
    }

    /// <summary>The PyTorch build for <paramref name="environment"/> on a computer with <paramref name="report"/>.</summary>
    public static string Backend(PythonEnvironment environment, GpuReport report)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return !environment.ProcessorOnly && SpeechDevices.CudaCard(report) is not null && PythonFiles.HasLock(environment, PythonPins.CudaBackend)
            ? PythonPins.CudaBackend
            : PythonPins.CpuBackend;
    }

    /// <summary>The installed interpreter (from the marker); null when the Python is not installed.</summary>
    public string? Interpreter
    {
        get
        {
            try
            {
                return File.Exists(layout.PythonMarker) && File.ReadAllText(layout.PythonMarker).Trim() is { Length: > 0 } path && File.Exists(path) ? path : null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    /// <summary>Whether the pinned Python is installed.</summary>
    public bool PythonReady => Interpreter is not null;

    /// <summary>
    /// Whether <paramref name="environment"/> is installed for this computer's PyTorch build and still points at the installed Python. A CUDA
    /// build counts as ready also when this run finds no usable NVIDIA card (the CUDA build runs on the processor too: a driver being
    /// reinstalled, or a card that changed, must not cost a 4 GB download); a processor build is installed again when a usable NVIDIA card
    /// appears. An environment whose base Python is no longer where it was made (a portable data folder moved, a new drive letter) is not
    /// ready: it is made again with the Python installed now.
    /// </summary>
    public bool EnvironmentReady(PythonEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        try
        {
            var marker = layout.EnvironmentMarker(environment.Key);
            if (!File.Exists(layout.EnvironmentPython(environment.Key)) || !File.Exists(marker) || !HomeIsCurrent(environment))
            {
                return false;
            }

            var recorded = File.ReadAllText(marker).Trim();
            var backend = Backend(environment);
            return recorded == Fingerprint(environment, backend)
                || (backend == PythonPins.CpuBackend && PythonFiles.HasLock(environment, PythonPins.CudaBackend)
                    && recorded == Fingerprint(environment, PythonPins.CudaBackend));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the base Python the environment's <c>pyvenv.cfg</c> names as its <c>home</c> is still there (and the Python is installed); true
    /// when the file names none.
    /// </summary>
    private bool HomeIsCurrent(PythonEnvironment environment)
    {
        var config = Path.Combine(layout.EnvironmentFolder(environment.Key), "pyvenv.cfg");
        if (!File.Exists(config))
        {
            return true;
        }

        var home = File.ReadLines(config).Select(l => l.Split('=', 2)).Where(p => p.Length == 2 && p[0].Trim().Equals("home", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[1].Trim()).FirstOrDefault();
        if (string.IsNullOrEmpty(home))
        {
            return true;
        }

        // Not compared with the interpreter's path itself: uv may name its Python by a version link folder. A moved data folder leaves a home
        // that no longer holds a Python at all.
        return Interpreter is not null && Directory.Exists(home)
            && Directory.EnumerateFiles(home, "python*").Any();
    }

    /// <summary>The fingerprint of an environment: Python version, PyTorch build, the lock's date and its whole text.</summary>
    public static string Fingerprint(PythonEnvironment environment, string backend) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('\n', PythonPins.PythonVersion, backend, PythonPins.ExcludeNewer, PythonFiles.Lock(environment, backend)))));

    /// <summary>
    /// The variables every uv run gets: where Python, the cache and the downloads go; no settings files, no progress bars; the user's own index,
    /// environment and Python path variables emptied (uv and Python take an empty one as not set; checked with uv 0.13.6).
    /// </summary>
    public IReadOnlyDictionary<string, string> UvEnvironment() => new Dictionary<string, string>
    {
        ["UV_PYTHON_INSTALL_DIR"] = layout.PythonFolder,
        ["UV_CACHE_DIR"] = layout.CacheFolder,
        ["UV_NO_CONFIG"] = "1",
        ["UV_NO_PROGRESS"] = "1",
        ["UV_PYTHON_INSTALL_BIN"] = "0",
        ["UV_PYTHON_INSTALL_REGISTRY"] = "0",
        ["UV_INDEX_URL"] = string.Empty,
        ["UV_EXTRA_INDEX_URL"] = string.Empty,
        ["UV_INDEX"] = string.Empty,
        ["UV_DEFAULT_INDEX"] = string.Empty,
        ["PIP_INDEX_URL"] = string.Empty,
        ["PIP_EXTRA_INDEX_URL"] = string.Empty,
        ["UV_TORCH_BACKEND"] = string.Empty,
        ["VIRTUAL_ENV"] = string.Empty,
        ["PYTHONHOME"] = string.Empty,
        ["PYTHONPATH"] = string.Empty,
    };

    /// <summary><c>uv python install</c> of the pinned version, into the tools folder only.</summary>
    public static IReadOnlyList<string> PythonInstallArguments() => ["python", "install", PythonPins.PythonVersion, "--no-bin", "--no-registry"];

    /// <summary><c>uv python find</c> of the pinned version (prints the interpreter's path).</summary>
    public static IReadOnlyList<string> PythonFindArguments() => ["python", "find", PythonPins.PythonVersion, "--no-python-downloads", "--managed-python"];

    /// <summary><c>uv venv</c> of an environment with <paramref name="interpreter"/> (an old one is replaced).</summary>
    public static IReadOnlyList<string> VenvArguments(string folder, string interpreter) =>
        ["venv", folder, "--python", interpreter, "--no-python-downloads", "--clear", "--no-config"];

    /// <summary><c>uv pip sync</c> of a lock file into an environment: exactly its packages, PyTorch from its own index for <paramref name="backend"/>.</summary>
    public static IReadOnlyList<string> SyncArguments(string lockFile, string python, string backend) =>
    [
        "pip", "sync", lockFile, "--python", python, "--torch-backend", backend, "--exclude-newer", PythonPins.ExcludeNewer, "--default-index",
        PythonPins.PackageIndex, "--no-config", "--no-python-downloads",
    ];

    /// <summary>Installs the pinned Python (uv must be installed).</summary>
    public async Task InstallPythonAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var uv = Uv();
        Directory.CreateDirectory(layout.PythonFolder);
        progress?.Report(new ToolProgress(0.1, "Python " + PythonPins.PythonVersion));
        await RunAsync(uv, PythonInstallArguments(), progress, 0.1, cancellationToken).ConfigureAwait(false);
        var lines = new List<string>();
        await RunAsync(uv, PythonFindArguments(), progress, 0.9, cancellationToken, lines).ConfigureAwait(false);
        var interpreter = lines.Select(l => l.Trim()).LastOrDefault(l => l.Length > 0 && File.Exists(l))
            ?? throw new ToolInstallException("uv installed Python but could not find it afterwards.") { Code = "extract" };
        await File.WriteAllTextAsync(layout.PythonMarker, interpreter, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Python {Version} installed for the Python voices", PythonPins.PythonVersion);
        progress?.Report(new ToolProgress(1, "Python " + PythonPins.PythonVersion));
    }

    /// <summary>Installs (or replaces) <paramref name="environment"/> for this computer's PyTorch build (the Python must be installed).</summary>
    public async Task InstallEnvironmentAsync(PythonEnvironment environment, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var uv = Uv();
        var interpreter = Interpreter ?? throw new ToolInstallException("Python for the Python voices is not installed.") { Code = "missing" };
        var backend = Backend(environment);
        var folder = layout.EnvironmentFolder(environment.Key);
        var marker = layout.EnvironmentMarker(environment.Key);
        if (File.Exists(marker))
        {
            File.Delete(marker);
        }

        Directory.CreateDirectory(layout.LocksFolder);
        var lockFile = Path.Combine(layout.LocksFolder, environment.Key + "-" + backend + ".txt");
        await File.WriteAllTextAsync(lockFile, PythonFiles.Lock(environment, backend), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Installing the Python environment {Environment} (PyTorch {Backend})", environment.Key, backend);
        progress?.Report(new ToolProgress(0.02, environment.Name));
        await RunAsync(uv, VenvArguments(folder, interpreter), progress, 0.02, cancellationToken).ConfigureAwait(false);
        await RunAsync(uv, SyncArguments(lockFile, layout.EnvironmentPython(environment.Key), backend), progress, 0.1, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(marker, Fingerprint(environment, backend), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("The Python environment {Environment} is installed (PyTorch {Backend})", environment.Key, backend);
        progress?.Report(new ToolProgress(1, environment.Name));
    }

    /// <summary>Whether uv's output tells of a passing Windows file lock (a virus scanner on a just-written file); worth one more try.</summary>
    public static bool IsPassingFileLock(string output) => FileLockMarkers.Any(m => output.Contains(m, StringComparison.OrdinalIgnoreCase));

    private string Uv() => tools.Status(ToolKind.Uv) is { Ready: true } status
        ? status.ExecutablePath
        : throw new ToolInstallException("uv is not installed. It is installed with the Python voices (after a confirmation).") { Code = "missing" };

    private async Task RunAsync(string uv, IReadOnlyList<string> arguments, IProgress<ToolProgress>? progress, double share, CancellationToken cancellationToken,
        List<string>? output = null)
    {
        logger.LogInformation("uv {Arguments}", string.Join(' ', arguments.Select(a => a.Contains(layout.Root, StringComparison.Ordinal) ? Path.GetFileName(a) : a)));
        for (var attempt = 0; ; attempt++)
        {
            output?.Clear();
            var start = new ProcessStart(uv, arguments, layout.Root) { Environment = UvEnvironment() };
            Directory.CreateDirectory(layout.Root);
            var result = await runner.RunAsync(start, line =>
            {
                output?.Add(line);
                if (line.Length > 0)
                {
                    progress?.Report(new ToolProgress(share, line.Length > 120 ? line[..120] : line));
                }
            }, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return;
            }

            if (attempt < LockRetries.Length && IsPassingFileLock(result.LastLines))
            {
                logger.LogWarning("uv met a file lock; trying again in {Seconds} s", LockRetries[attempt].TotalSeconds);
                await Task.Delay(LockRetries[attempt], cancellationToken).ConfigureAwait(false);
                continue;
            }

            logger.LogError("uv failed (exit code {Code}): {Output}", result.ExitCode, ChildOutput.Summary(result.LastLines));
            throw new ToolInstallException("uv failed: " + ChildOutput.Summary(result.LastLines)) { Code = "install" };
        }
    }
}
