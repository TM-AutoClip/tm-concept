using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Hardware;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;
using TMConcept.Speech.Python;
using TMConcept.Tests.Desktop;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Speech;

/// <summary>
/// The install side of the Python voices without network: which PyTorch build a computer gets, the exact uv command lines, the lock files
/// that ship with the app, and a whole install of the Python and an environment through a fake process runner (uv is never run).
/// </summary>
public sealed class PythonInstallTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static readonly GpuReport Nvidia = new([new GpuAdapter(GpuVendor.Nvidia, "RTX", 12 * GiB)], true, true);

    private static readonly GpuReport Amd = new([new GpuAdapter(GpuVendor.Amd, "Radeon", 16 * GiB)], false, true);

    [Fact]
    public void AnNvidiaCardWithItsDriverGetsTheCudaBuildEverythingElseTheProcessorBuild()
    {
        PythonInstaller.Backend(PythonEnvironments.Xtts, Nvidia).ShouldBe(PythonPins.CudaBackend);
        PythonInstaller.Backend(PythonEnvironments.VoxCpm, Nvidia).ShouldBe("cu128");
        PythonInstaller.Backend(PythonEnvironments.Xtts, Amd).ShouldBe(PythonPins.CpuBackend, "PyTorch has no AMD build for these voices on Windows");
        PythonInstaller.Backend(PythonEnvironments.Xtts, GpuReport.None).ShouldBe("cpu");
        PythonInstaller.Backend(PythonEnvironments.Xtts, Nvidia with { CudaDriver = false }).ShouldBe("cpu", "no driver, no CUDA");
        PythonInstaller.Backend(PythonEnvironments.Mms, Nvidia).ShouldBe("cpu", "the small MMS always runs on the processor");
    }

    [Fact]
    public void TheUvCommandsInstallOnlyIntoTheToolsFolderAndOnlyThePinnedVersions()
    {
        PythonInstaller.PythonInstallArguments().ShouldBe(["python", "install", "3.11.18", "--no-bin", "--no-registry"]);
        PythonInstaller.PythonFindArguments().ShouldContain("--no-python-downloads");
        PythonInstaller.VenvArguments("env", "py.exe").ShouldBe(["venv", "env", "--python", "py.exe", "--no-python-downloads", "--clear", "--no-config"]);

        var sync = PythonInstaller.SyncArguments("lock.txt", "python.exe", "cu128");
        sync.Take(3).ShouldBe(["pip", "sync", "lock.txt"], "sync: exactly the lock, nothing else stays in the environment");
        sync.ShouldContain("--torch-backend");
        sync[sync.ToList().IndexOf("--torch-backend") + 1].ShouldBe("cu128");
        sync[sync.ToList().IndexOf("--exclude-newer") + 1].ShouldBe(PythonPins.ExcludeNewer);
        sync[sync.ToList().IndexOf("--default-index") + 1].ShouldBe("https://pypi.org/simple");
        sync.ShouldContain("--no-config", "a user's uv.toml must not change what is installed");
    }

    [Fact]
    public void EveryEnvironmentShipsAFullyPinnedLockPerBuild()
    {
        foreach (var environment in PythonEnvironments.All)
        {
            string[] backends = environment.ProcessorOnly ? ["cpu"] : ["cu128", "cpu"];
            foreach (var backend in backends)
            {
                PythonFiles.HasLock(environment, backend).ShouldBeTrue(environment.Key + " " + backend);
                var packages = PythonFiles.Lock(environment, backend).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
                packages.Count.ShouldBeGreaterThan(10);
                packages.ShouldAllBe(p => p.Contains("==", StringComparison.Ordinal) || p.Contains(" @ https://github.com/", StringComparison.Ordinal),
                    "every package at an exact version (or a pinned commit archive)");
                packages.ShouldNotContain(p => p.StartsWith("torchcodec", StringComparison.Ordinal), "torchcodec has no Windows CUDA build and is never used");
                packages.ShouldContain(p => p.StartsWith("torch==2.8.0+" + backend, StringComparison.Ordinal), environment.Key + ": the PyTorch of the build");
            }
        }

        PythonFiles.HasLock(PythonEnvironments.Mms, "cu128").ShouldBeFalse();
        PythonFiles.Lock(PythonEnvironments.VoxCpm, "cpu").ShouldContain("trnorm @ https://github.com/ozguradmin/ahenk-turkish-tts/archive/8981df9354a6dc95a3a2b4730b7fbff196dda219.zip");
        PythonInstaller.Fingerprint(PythonEnvironments.Xtts, "cu128").ShouldNotBe(PythonInstaller.Fingerprint(PythonEnvironments.Xtts, "cpu"));
    }

    [Fact]
    public void TheWorkerScriptsShipWithTheApp()
    {
        PythonFiles.Scripts.ShouldBe(["adapter_f5.py", "adapter_mms.py", "adapter_voxcpm.py", "adapter_xtts.py", "f5_ref_tr.wav", "tmc_common.py", "worker.py"]);
        System.Text.Encoding.UTF8.GetString(PythonFiles.Read("worker.py")).ShouldContain("HF_HUB_OFFLINE");
    }

    [Fact]
    public async Task ThePythonAndAnEnvironmentAreInstalledWithUvAndMarkedForTheirBuild()
    {
        using var paths = new TempPaths();
        var layout = new PythonLayout(paths);
        var runner = new FakeUv(layout);
        var installer = new PythonInstaller(layout, new FakeTools(Path.Combine(paths.ToolsDirectory, "uv", "uv.exe")), runner, new FakeGpu(Nvidia),
            NullLogger<PythonInstaller>.Instance);

        installer.PythonReady.ShouldBeFalse();
        installer.EnvironmentReady(PythonEnvironments.Xtts).ShouldBeFalse();
        await Should.ThrowAsync<ToolInstallException>(() => installer.InstallEnvironmentAsync(PythonEnvironments.Xtts, null, CancellationToken.None));

        await installer.InstallPythonAsync(null, CancellationToken.None);
        installer.PythonReady.ShouldBeTrue();
        await installer.InstallEnvironmentAsync(PythonEnvironments.Xtts, null, CancellationToken.None);
        installer.EnvironmentReady(PythonEnvironments.Xtts).ShouldBeTrue();

        runner.Runs.Select(r => string.Join(' ', r.Arguments.Take(2))).ShouldBe(["python install", "python find", $"venv {layout.EnvironmentFolder("xtts")}", "pip sync"]);
        var sync = runner.Runs[^1];
        sync.Arguments[sync.Arguments.ToList().IndexOf("--torch-backend") + 1].ShouldBe("cu128");
        File.ReadAllText(sync.Arguments[2]).ShouldBe(PythonFiles.Lock(PythonEnvironments.Xtts, "cu128"));
        foreach (var run in runner.Runs)
        {
            run.Environment["UV_PYTHON_INSTALL_DIR"].ShouldBe(layout.PythonFolder);
            run.Environment["UV_CACHE_DIR"].ShouldBe(layout.CacheFolder);
            run.Environment["UV_NO_CONFIG"].ShouldBe("1");
            run.Environment["UV_INDEX_URL"].ShouldBeEmpty("a user's own index is not used");
            Path.GetFullPath(run.Program).ShouldStartWith(Path.GetFullPath(paths.ToolsDirectory), Case.Insensitive, "the app's own uv, never the system's");
        }

        // Another computer (or the card is gone): the marker no longer matches, the environment is installed again (the processor build).
        var onAmd = new PythonInstaller(layout, new FakeTools(string.Empty), runner, new FakeGpu(Amd), NullLogger<PythonInstaller>.Instance);
        onAmd.EnvironmentReady(PythonEnvironments.Xtts).ShouldBeFalse();
        onAmd.Backend(PythonEnvironments.Xtts).ShouldBe("cpu");
    }

    [Fact]
    public async Task AFailingUvEndsTheInstallWithItsOwnReasonAndNoMarker()
    {
        using var paths = new TempPaths();
        var layout = new PythonLayout(paths);
        var runner = new FakeUv(layout) { FailOn = "sync" };
        var installer = new PythonInstaller(layout, new FakeTools(Path.Combine(paths.ToolsDirectory, "uv", "uv.exe")), runner, new FakeGpu(Amd),
            NullLogger<PythonInstaller>.Instance);
        await installer.InstallPythonAsync(null, CancellationToken.None);

        var failure = await Should.ThrowAsync<ToolInstallException>(() => installer.InstallEnvironmentAsync(PythonEnvironments.F5, null, CancellationToken.None));
        failure.Message.ShouldContain("No solution found");
        File.Exists(layout.EnvironmentMarker("f5")).ShouldBeFalse();
        installer.EnvironmentReady(PythonEnvironments.F5).ShouldBeFalse();
        PythonInstaller.IsPassingFileLock("error: failed to rename (os error 32)").ShouldBeTrue();
        PythonInstaller.IsPassingFileLock("No solution found").ShouldBeFalse();
    }

    [Fact]
    public void UvIsPinnedForEveryPlatformTheAppRunsOn()
    {
        var rows = ToolManifest.Archives.Where(a => a.Tool == ToolKind.Uv).ToList();
        rows.Select(a => a.Rid).ShouldBe(["win-x64", "linux-x64", "linux-arm64"], ignoreOrder: true);
        rows.ShouldAllBe(a => a.Version == ToolManifest.UvVersion && a.Sha256.Length == 64 && a.Url.Host == "github.com");
        ToolManifest.Libraries.ShouldContain(ToolKind.Uv, "uv is never taken from the system PATH");
    }

    /// <summary>uv played: <c>python find</c> prints an interpreter it creates, <c>venv</c> creates the environment's python, nothing else happens.</summary>
    private sealed class FakeUv(PythonLayout layout) : IProcessRunner
    {
        public List<ProcessStart> Runs { get; } = [];

        public string FailOn { get; init; } = string.Empty;

        public Task<ProcessResult> RunAsync(ProcessStart start, Action<string>? onLine, CancellationToken cancellationToken)
        {
            Runs.Add(start);
            var command = string.Join(' ', start.Arguments.Take(2));
            if (FailOn.Length > 0 && command.Contains(FailOn, StringComparison.Ordinal))
            {
                return Task.FromResult(new ProcessResult(1, "error: No solution found when resolving dependencies"));
            }

            if (command == "python find")
            {
                var interpreter = Path.Combine(layout.PythonFolder, "cpython-3.11.18", "python.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(interpreter)!);
                File.WriteAllText(interpreter, "fake");
                onLine?.Invoke(interpreter);
            }
            else if (start.Arguments[0] == "venv")
            {
                var python = layout.EnvironmentPython(Path.GetFileName(start.Arguments[1])["env-".Length..]);
                Directory.CreateDirectory(Path.GetDirectoryName(python)!);
                File.WriteAllText(python, "fake");
            }

            return Task.FromResult(new ProcessResult(0, string.Empty));
        }
    }

    private sealed class FakeTools(string uv) : IToolLocator
    {
        public ToolStatus Status(ToolKind tool) =>
            tool == ToolKind.Uv && uv.Length > 0 ? new ToolStatus(tool, true, ToolManifest.UvVersion, uv, "app") : new ToolStatus(tool, false, string.Empty, string.Empty, string.Empty);
    }
}
