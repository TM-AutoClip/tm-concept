using System.Buffers.Binary;
using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.Hardware;
using TMConcept.Core.Images;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Components;
using TMConcept.Engines.Images;
using TMConcept.Engines.Tools;
using TMConcept.Tests.Desktop;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Engines;

/// <summary>
/// The stable-diffusion.cpp build per computer: the choice matrix, the pinned rows of every build, which files a component waits for, the
/// memory flags by card size, the engine's library environment and error texts, and the blank-picture check. sd-cli is never run (a fake
/// runner writes the picture), nothing runs on a graphics card.
/// </summary>
public sealed class ImageBackendTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static readonly GpuAdapter Nvidia = new(GpuVendor.Nvidia, "NVIDIA GeForce RTX 3060", 12 * GiB);
    private static readonly GpuAdapter Amd = new(GpuVendor.Amd, "AMD Radeon RX 7800 XT", 16 * GiB);
    private static readonly GpuAdapter Intel = new(GpuVendor.Intel, "Intel UHD Graphics", 128L * 1024 * 1024);
    private static readonly GpuAdapter Virtual = new(GpuVendor.Other, "VMware SVGA 3D", 32L * GiB);

    public static TheoryData<string, ImageAcceleration, ImageBackend> Matrix() => new()
    {
        // Automatic: NVIDIA with its driver → CUDA on Windows, Vulkan on Linux (no CUDA download); AMD/Intel → Vulkan; nothing → processor.
        { "nvidia+cuda+vulkan win", ImageAcceleration.Automatic, ImageBackend.Cuda },
        { "nvidia+cuda+vulkan linux", ImageAcceleration.Automatic, ImageBackend.Vulkan },
        { "nvidia+vulkan win", ImageAcceleration.Automatic, ImageBackend.Vulkan },
        { "amd+vulkan win", ImageAcceleration.Automatic, ImageBackend.Vulkan },
        { "amd win", ImageAcceleration.Automatic, ImageBackend.Cpu },
        { "intel+amd+vulkan win", ImageAcceleration.Automatic, ImageBackend.Vulkan },
        { "none win", ImageAcceleration.Automatic, ImageBackend.Cpu },
        { "none+vulkan win", ImageAcceleration.Automatic, ImageBackend.Cpu },
        // A virtual or unknown adapter (VMware SVGA) lists under Vulkan but has no usable device: the processor.
        { "other+vulkan win", ImageAcceleration.Automatic, ImageBackend.Cpu },
        { "other+amd+vulkan linux", ImageAcceleration.Automatic, ImageBackend.Vulkan },
        // A chosen build is used when it has a download here, even against the card (the user decides); CUDA on Linux falls back.
        { "amd+vulkan win", ImageAcceleration.Cuda, ImageBackend.Cuda },
        { "amd+vulkan linux", ImageAcceleration.Cuda, ImageBackend.Vulkan },
        { "nvidia+cuda+vulkan win", ImageAcceleration.Vulkan, ImageBackend.Vulkan },
        { "nvidia+cuda+vulkan win", ImageAcceleration.Cpu, ImageBackend.Cpu },
        { "none win", ImageAcceleration.Vulkan, ImageBackend.Vulkan },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void TheBuildFollowsTheCardTheDriversAndTheSetting(string computer, ImageAcceleration setting, ImageBackend expected)
    {
        var (report, rid) = Computer(computer);

        var choice = ImageBackendSelector.Choose(setting, report, b => ImageBackendSelector.Tools(b).All(t => ToolManifest.Archives.Any(a => a.Tool == t && a.Rid == rid)));

        choice.Backend.ShouldBe(expected);
        choice.Setting.ShouldBe(setting);
        switch (expected)
        {
            case ImageBackend.Cpu:
                choice.Device.ShouldBeNull();
                break;
            case ImageBackend.Cuda:
                choice.Device?.Vendor.ShouldBe(GpuVendor.Nvidia);
                break;
            default:
                choice.Device.ShouldBe(report.Largest());
                break;
        }
    }

    [Fact]
    public void LinuxArm64HasNoBuildAndGetsTheUnsupportedProcessorChoice()
    {
        var choice = ImageBackendSelector.Choose(ImageAcceleration.Automatic, new GpuReport([Amd], false, true), _ => false);

        choice.Backend.ShouldBe(ImageBackend.Cpu);
    }

    [Fact]
    public void EveryBuildIsPinnedToItsReleaseWithItsHashAndSize()
    {
        var expected = new Dictionary<(ToolKind, string), (string File, string Sha256, long Bytes, string Cli)>
        {
            [(ToolKind.ImageRuntime, "win-x64")] = ("sd-master-532d2c1-bin-win-cuda12-x64.zip", "b087219145a382cc1b1e826318289e1f672b9bb6965977734ef0deb85fe2d03a", 412_365_497, "sd-cli.exe"),
            [(ToolKind.ImageRuntimeCuda, "win-x64")] = ("cudart-sd-bin-win-cu12-x64.zip", "fe20366827d357c00797eebb58244dddab7fd9a348d70090c3871004c320f38d", 563_452_046, "cudart64_12.dll"),
            [(ToolKind.ImageRuntimeVulkan, "win-x64")] = ("sd-master-532d2c1-bin-win-vulkan-x64.zip", "3e66222c87ca27074e1fd8ce79706eef17a8aaf39949906aa1bee7e060f22ecc", 30_312_081, "sd-cli.exe"),
            [(ToolKind.ImageRuntimeCpu, "win-x64")] = ("sd-master-532d2c1-bin-win-cpu-x64.zip", "abef332cfca7861cc567910e79bedb70bfb2920bb4eedac97fcbb6db477f1596", 18_358_541, "sd-cli.exe"),
            [(ToolKind.ImageRuntimeVulkan, "linux-x64")] = ("sd-master-3be2378-bin-Linux-Ubuntu-24.04-x86_64-vulkan.zip", "7608901016fb4a348bc5b75c46fc971085dadadf44e6f30daa97301844b1ce2e", 37_675_781, "sd-cli"),
            [(ToolKind.ImageRuntimeCpu, "linux-x64")] = ("sd-master-3be2378-bin-Linux-Ubuntu-24.04-x86_64.zip", "01e1c773c1bc2a6276952f6dbd13294e58b7255c659b4a770086934c4e8fad3a", 27_322_680, "sd-cli"),
        };
        var rows = ToolManifest.Archives.Where(a => ImageBackendSelector.AllTools.Contains(a.Tool)).ToList();

        rows.Count.ShouldBe(expected.Count);
        foreach (var row in rows)
        {
            var (file, sha256, bytes, cli) = expected[(row.Tool, row.Rid)];
            // Windows: the newest release; Linux: the last one built on Ubuntu 24.04 (later ones need glibc 2.43). Both know FLUX.3 (build 968+).
            var tag = row.Rid.StartsWith("win", StringComparison.Ordinal) ? ToolManifest.SdCppTag : ToolManifest.SdCppLinuxTag;
            row.Version.ShouldBe(tag);
            SdCppImageEngine.BuildNumber(row.Version).ShouldBeGreaterThanOrEqualTo(LocalImageModels.Flux3Build);
            row.Url.Scheme.ShouldBe("https");
            row.Url.ToString().ShouldBe($"https://github.com/leejet/stable-diffusion.cpp/releases/download/{tag}/{file}");
            row.Sha256.ShouldBe(sha256);
            row.ApproximateBytes.ShouldBe(bytes);
            row.ExecutablePath.ShouldBe(cli, "the zips are flat");
            row.Root.ShouldBeEmpty();
            ToolManifest.Libraries.ShouldContain(row.Tool, "a build is used only from the app's own copy");
        }

        ToolManifest.Archives.ShouldNotContain(a => ImageBackendSelector.AllTools.Contains(a.Tool) && a.Rid == "linux-arm64");
    }

    [Theory]
    [InlineData(ImageBackend.Cuda, "win-x64", new[] { ToolKind.ImageRuntime, ToolKind.ImageRuntimeCuda })]
    [InlineData(ImageBackend.Vulkan, "win-x64", new[] { ToolKind.ImageRuntimeVulkan })]
    [InlineData(ImageBackend.Cpu, "win-x64", new[] { ToolKind.ImageRuntimeCpu })]
    [InlineData(ImageBackend.Vulkan, "linux-x64", new[] { ToolKind.ImageRuntimeVulkan })]
    [InlineData(ImageBackend.Cpu, "linux-x64", new[] { ToolKind.ImageRuntimeCpu })]
    public void TheComponentAndTheImageModelWaitForExactlyTheChosenBuild(ImageBackend backend, string rid, ToolKind[] needed)
    {
        using var paths = new TempPaths();
        var installer = Installer(paths, rid);
        var (runtime, model) = Components(paths, installer, backend);
        Weights(paths, LocalImageModels.Flux2Klein);

        runtime.Ready.ShouldBeFalse();
        model.Ready.ShouldBeFalse();
        runtime.Supported.ShouldBeTrue();
        runtime.Name.ShouldContain(backend == ImageBackend.Cpu ? "CPU" : backend == ImageBackend.Cuda ? "CUDA" : "Vulkan");
        runtime.Downloads().Count.ShouldBe(needed.Length);

        // Every other build installed: still not ready.
        foreach (var other in ImageBackendSelector.AllTools.Except(needed).Where(t => installer.Archive(t) is not null))
        {
            Install(installer, other);
        }

        runtime.Ready.ShouldBeFalse();
        model.Ready.ShouldBeFalse();

        // All but the last file of the chosen build (for CUDA: sd-cli without the CUDA runtime libraries): not ready.
        foreach (var tool in needed.SkipLast(1))
        {
            Install(installer, tool);
        }

        model.Ready.ShouldBeFalse(needed.Length > 1 ? "the CUDA runtime libraries are part of the CUDA build" : "nothing of the build is there");
        Install(installer, needed[^1]);
        runtime.Ready.ShouldBeTrue();
        model.Ready.ShouldBeTrue();
        runtime.Downloads().ShouldBeEmpty();
        runtime.Folders().Count.ShouldBeGreaterThanOrEqualTo(needed.Length, "every build's folder is listed and removed with it");
    }

    [Fact]
    public void ChangingTheSettingMakesTheComponentWaitForTheNewBuild()
    {
        using var paths = new TempPaths();
        var installer = Installer(paths, "win-x64");
        var setting = new FakeSetting { Value = ImageAcceleration.Automatic };
        var selector = new ImageBackendSelector(new FakeGpu(new GpuReport([Amd], false, true)), setting, installer);
        var runtime = new ImageRuntimeComponent(installer, selector);
        Install(installer, ToolKind.ImageRuntimeVulkan);
        runtime.Ready.ShouldBeTrue();

        setting.Value = ImageAcceleration.Cpu;

        runtime.Ready.ShouldBeFalse();
        runtime.Downloads().Single().Name.ShouldContain("CPU");
    }

    [Fact]
    public void AnOldCudaInstallCountsAsInstalledSoAVersionUpdatePlansTheBuildChosenNow()
    {
        // 0.1.15 put the CUDA build in tools/sd-cpp; on an AMD card the update after a new version must still see the image runtime as
        // installed before (the update dialog re-plans only those) and then install the Vulkan build.
        using var paths = new TempPaths();
        var installer = Installer(paths, "win-x64");
        var selector = new ImageBackendSelector(new FakeGpu(new GpuReport([Amd], false, true)), new FakeSetting(), installer);
        var runtime = new ImageRuntimeComponent(installer, selector);
        runtime.WasInstalled.ShouldBeFalse();

        Directory.CreateDirectory(Path.Combine(installer.ToolRoot(ToolKind.ImageRuntime), "old-version"));
        runtime.WasInstalled.ShouldBeFalse("an empty folder is no install");
        File.WriteAllText(Path.Combine(installer.ToolRoot(ToolKind.ImageRuntime), "old-version", "sd-cli.exe"), "old");

        runtime.WasInstalled.ShouldBeTrue();
        runtime.Ready.ShouldBeFalse();
        runtime.Downloads().Single().Name.ShouldContain("Vulkan");
    }

    [Fact]
    public void TheCommandLineAlwaysHasFlashAttentionAndTheCardsFlags()
    {
        var arguments = SdCppImageEngine.Arguments(LocalImageModels.ZImageTurbo, "a map", null, 576, 1024, 42, "out.png", f => "/w/" + f.ExecutablePath, []);

        arguments.ShouldNotContain("--diffusion-fa", "deprecated since master-1043");
        arguments[arguments.ToList().IndexOf("--flash-attn") + 1].ShouldBe("auto,diffusion=on");
        arguments.ShouldNotContain("--max-vram");
        SdCppImageEngine.Arguments(LocalImageModels.ZImageTurbo, "a map", null, 576, 1024, 42, "out.png", f => f.ExecutablePath, ["--vae-tiling", "--max-vram", "-1"])
            .TakeLast(3).ShouldBe(["--vae-tiling", "--max-vram", "-1"]);
    }

    [Fact]
    public void WindowsFindsTheLibrariesThroughThePathAndLinuxThroughLdLibraryPath()
    {
        var windows = SdCppImageEngine.LibraryEnvironment(true, @"C:\t\sd-cpp\x", [@"C:\t\sd-cpp-cuda\x"], @"C:\Windows", "/ignored");
        windows.ShouldBe(new Dictionary<string, string> { ["PATH"] = @"C:\t\sd-cpp-cuda\x;C:\t\sd-cpp\x;C:\Windows" });

        var linux = SdCppImageEngine.LibraryEnvironment(false, "/t/sd-cpp-vulkan/x", [], "/usr/bin", "/opt/lib");
        linux.ShouldBe(new Dictionary<string, string> { ["LD_LIBRARY_PATH"] = "/t/sd-cpp-vulkan/x:/opt/lib" });
        SdCppImageEngine.LibraryEnvironment(false, "/t/x", [], null, null)["LD_LIBRARY_PATH"].ShouldBe("/t/x");
    }

    [Fact]
    public void AMissingLibraryAndOtherCodesAreExplained()
    {
        var dll = SdCppImageEngine.Failure("Z-Image Turbo", ImageBackend.Cuda, -1073741515, string.Empty);
        dll.ShouldContain("a required library (DLL) of the NVIDIA CUDA build could not be loaded");
        dll.ShouldContain("0xC0000135");
        dll.ShouldContain("Visual C++ 2015–2022", Case.Sensitive, "every build imports the runtime: it is the first cause named");
        dll.ShouldContain(SdCppImageEngine.VisualCppRedistributable);
        dll.ShouldContain("Image acceleration");

        // The processor build fails the same way without the runtime: another build is no advice there.
        var cpu = SdCppImageEngine.Failure("Z-Image Turbo", ImageBackend.Cpu, -1073741515, string.Empty);
        cpu.ShouldContain("Visual C++");
        cpu.ShouldNotContain("Image acceleration");
        SdCppImageEngine.VisualCppMissing("Z-Image Turbo", ["vcruntime140_1.dll"]).ShouldContain("missing: vcruntime140_1.dll");

        var other = SdCppImageEngine.Failure("Z-Image Turbo", ImageBackend.Vulkan, -1, string.Empty);
        other.ShouldContain("0xFFFFFFFF");
        other.ShouldContain("without any output");
        SdCppImageEngine.Failure("Z-Image Turbo", ImageBackend.Cpu, 1, "out of memory").ShouldEndWith("(0x00000001): out of memory");
        SdCppImageEngine.Blank("FLUX.2 klein 4B", ImageBackend.Vulkan).ShouldContain("single colour");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheEngineRunsTheChosenBuildAndTurnsABlankPictureIntoAFailure(bool blank)
    {
        using var paths = new TempPaths();
        var installer = Installer(paths, "win-x64");
        Install(installer, ToolKind.ImageRuntimeVulkan);
        var store = new LocalImageStore(paths, new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance), NullLogger<LocalImageStore>.Instance);
        Weights(paths, LocalImageModels.Flux2Klein);
        var selector = new ImageBackendSelector(new FakeGpu(new GpuReport([Amd], false, true)), new FakeSetting(), installer);
        var runner = new PictureRunner(blank ? Png(8, 8, (_, _) => [0, 0, 0]) : Png(8, 8, (x, y) => [(byte)(x * 30), (byte)(y * 30), 7]));
        var engine = Engine(installer, store, selector, new FakeProbe(), runner);
        var output = Path.Combine(paths.WorkDirectory, "scene-1.png");
        var request = new ImageRequest("a map", 1280, 720, output,
            new ResolvedModel(new CatalogModel { Provider = ModelProviders.Local, Id = LocalImageModels.Flux2Klein.Id }, new ApiKey(), string.Empty));

        if (blank)
        {
            var error = await Should.ThrowAsync<InvalidOperationException>(() => engine.GenerateAsync(request, CancellationToken.None));
            error.Message.ShouldContain("blank picture");
            File.Exists(output).ShouldBeFalse();
        }
        else
        {
            (await engine.GenerateAsync(request, CancellationToken.None)).File.ShouldBe(output);
        }

        var start = runner.Start.ShouldNotBeNull();
        start.Program.ShouldBe(installer.Status(ToolKind.ImageRuntimeVulkan).ExecutablePath);
        start.Arguments.ShouldContain("--flash-attn");
        // A 16 GB card with 13 GiB free holds the 2.5 GB FLUX.2 klein: on the card, tiled VAE, 1 GiB kept free.
        string.Join(' ', start.Arguments.TakeLast(3)).ShouldBe("--vae-tiling --max-vram -1");
        start.Arguments.ShouldNotContain("--offload-to-cpu");
        start.Arguments.ShouldNotContain("--mmap");
        runner.InsideLease.ShouldBe([true], "sd-cli runs inside the model's graphics card lease");
        start.Environment.Keys.ShouldBe([OperatingSystem.IsWindows() ? "PATH" : "LD_LIBRARY_PATH"]);
    }

    [Fact]
    public async Task TheCudaBuildWithoutItsRuntimeLibrariesIsReportedBeforeAnythingRuns()
    {
        using var paths = new TempPaths();
        var installer = Installer(paths, "win-x64");
        Install(installer, ToolKind.ImageRuntime);
        var store = new LocalImageStore(paths, new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance), NullLogger<LocalImageStore>.Instance);
        Weights(paths, LocalImageModels.Flux2Klein);
        var selector = new ImageBackendSelector(new FakeGpu(new GpuReport([Nvidia], true, true)), new FakeSetting(), installer);
        var runner = new PictureRunner([]);
        var engine = Engine(installer, store, selector, new FakeProbe(), runner);
        var request = new ImageRequest("a map", 512, 512, Path.Combine(paths.WorkDirectory, "a.png"),
            new ResolvedModel(new CatalogModel { Provider = ModelProviders.Local, Id = LocalImageModels.Flux2Klein.Id }, new ApiKey(), string.Empty));

        var error = await Should.ThrowAsync<ToolInstallException>(() => engine.GenerateAsync(request, CancellationToken.None));

        error.Message.ShouldContain(nameof(ToolKind.ImageRuntimeCuda));
        runner.Start.ShouldBeNull();
    }

    [Fact]
    public async Task AMissingVisualCppRuntimeOrDriverIsFoundBeforeAnythingRuns()
    {
        using var paths = new TempPaths();
        var installer = Installer(paths, "win-x64");
        Install(installer, ToolKind.ImageRuntimeVulkan);
        var store = new LocalImageStore(paths, new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance), NullLogger<LocalImageStore>.Instance);
        Weights(paths, LocalImageModels.Flux2Klein);
        var model = new ResolvedModel(new CatalogModel { Provider = ModelProviders.Local, Id = LocalImageModels.Flux2Klein.Id }, new ApiKey(), string.Empty);
        var runner = new PictureRunner([]);
        var selector = new ImageBackendSelector(new FakeGpu(new GpuReport([Amd], false, true)), new FakeSetting(), installer);

        var noRuntime = Engine(installer, store, selector, new FakeProbe { Missing = ["vcruntime140_1.dll", "msvcp140.dll"] }, runner);
        noRuntime.Problem(model).ShouldBe(new ImageEngineProblem(ImageEngineProblems.VisualCppRuntime, "vcruntime140_1.dll, msvcp140.dll"));
        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            noRuntime.GenerateAsync(new ImageRequest("a map", 512, 512, Path.Combine(paths.WorkDirectory, "a.png"), model), CancellationToken.None));
        error.Message.ShouldContain(SdCppImageEngine.VisualCppRedistributable);
        runner.Start.ShouldBeNull("sd-cli is not started without the runtime");

        // The Vulkan build chosen by hand without a Vulkan driver, the CUDA build on an AMD card: said before anything is paid for.
        var vulkan = new ImageBackendSelector(new FakeGpu(new GpuReport([Amd], false, false)), new FakeSetting { Value = ImageAcceleration.Vulkan }, installer);
        var noDriver = Engine(installer, store, vulkan, new FakeProbe { Vulkan = false }, runner);
        noDriver.Problem(model).ShouldBe(new ImageEngineProblem(ImageEngineProblems.Images, SdCppImageEngine.DriverMissing(ImageBackend.Vulkan)));
        Engine(installer, store, selector, new FakeProbe(), runner).Problem(model).ShouldBeNull();
    }

    [Fact]
    public async Task UnderAutomaticAFailedGpuBuildIsFollowedByTheInstalledProcessorBuild()
    {
        var notices = new RecordingNotices();
        using var paths = new TempPaths();
        var installer = Installer(paths, "win-x64");
        Install(installer, ToolKind.ImageRuntimeVulkan);
        Install(installer, ToolKind.ImageRuntimeCpu);
        var store = new LocalImageStore(paths, new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance), NullLogger<LocalImageStore>.Instance);
        Weights(paths, LocalImageModels.Flux2Klein);
        var selector = new ImageBackendSelector(new FakeGpu(new GpuReport([Amd], false, true)), new FakeSetting(), installer);
        var vulkan = installer.Status(ToolKind.ImageRuntimeVulkan).ExecutablePath;
        var runner = new PictureRunner(Png(8, 8, (x, y) => [(byte)(x * 30), (byte)(y * 30), 7])) { FailFor = vulkan };
        var engine = Engine(installer, store, selector, new FakeProbe(), runner, notices: notices);
        var model = new ResolvedModel(new CatalogModel { Provider = ModelProviders.Local, Id = LocalImageModels.Flux2Klein.Id }, new ApiKey(), string.Empty);

        (await engine.GenerateAsync(new ImageRequest("a map", 512, 512, Path.Combine(paths.WorkDirectory, "1.png"), model), CancellationToken.None)).CostUsd.ShouldBe(0);
        runner.Programs.ShouldBe([vulkan, installer.Status(ToolKind.ImageRuntimeCpu).ExecutablePath]);

        // 0.1.18: the switch to the processor build (the same model, much slower) is said on screen, no longer silent.
        notices.Kinds.ShouldContain(MemoryNoticeKind.ProcessorFallback);
        runner.Runs.ShouldAllBe(r => r.Contains(store.File(LocalImageModels.Flux2Klein, LocalImageModels.Flux2Klein.Diffusion)), "the model never changes");

        // The next picture of the session goes to the processor build at once.
        await engine.GenerateAsync(new ImageRequest("ships", 512, 512, Path.Combine(paths.WorkDirectory, "2.png"), model), CancellationToken.None);
        runner.Programs.Count.ShouldBe(3);
        runner.Programs[^1].ShouldBe(installer.Status(ToolKind.ImageRuntimeCpu).ExecutablePath);

        // A build chosen by hand is not replaced: its failure is the user's answer.
        var chosen = new ImageBackendSelector(new FakeGpu(new GpuReport([Amd], false, true)), new FakeSetting { Value = ImageAcceleration.Vulkan }, installer);
        var manual = Engine(installer, store, chosen, new FakeProbe(), runner);
        await Should.ThrowAsync<InvalidOperationException>(() =>
            manual.GenerateAsync(new ImageRequest("a map", 512, 512, Path.Combine(paths.WorkDirectory, "3.png"), model), CancellationToken.None));
        runner.Programs.Count.ShouldBe(4);
    }

    [Fact]
    public async Task ARunThatEndsOutOfMemoryStepsDownLevelByLevelWithTheSameModel()
    {
        using var paths = new TempPaths();
        var installer = Installer(paths, "win-x64");
        Install(installer, ToolKind.ImageRuntimeVulkan);
        var store = new LocalImageStore(paths, new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance), NullLogger<LocalImageStore>.Instance);
        Weights(paths, LocalImageModels.Flux2Klein);
        var selector = new ImageBackendSelector(new FakeGpu(new GpuReport([Amd], false, true)), new FakeSetting { Value = ImageAcceleration.Vulkan }, installer);
        var runner = new PictureRunner(Png(8, 8, (x, y) => [(byte)(x * 30), (byte)(y * 30), 7])) { OutOfMemoryRuns = 1 };
        var engine = Engine(installer, store, selector, new FakeProbe(), runner);
        var model = new ResolvedModel(new CatalogModel { Provider = ModelProviders.Local, Id = LocalImageModels.Flux2Klein.Id }, new ApiKey(), string.Empty);

        await engine.GenerateAsync(new ImageRequest("a map", 512, 512, Path.Combine(paths.WorkDirectory, "1.png"), model), CancellationToken.None);

        runner.Runs.Count.ShouldBe(2);
        runner.Runs[0].ShouldNotContain("--mmap");
        runner.Runs[1].ShouldContain("--mmap", "one level lower: what does not fit stays in main memory");
        runner.Runs[1].ShouldNotContain("--offload-to-cpu", "sd.cpp places the weights itself (auto-fit)");
        runner.InsideLease.ShouldBe([true, true]);

        // Out of memory at every level: card, main memory, disk, disk with the text encoder on the processor; then sd-cli's own words. The
        // model (its files) never changes, only the options.
        var always = new PictureRunner([]) { OutOfMemoryRuns = 9 };
        var failing = Engine(installer, store, selector, new FakeProbe(), always);
        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            failing.GenerateAsync(new ImageRequest("a map", 512, 512, Path.Combine(paths.WorkDirectory, "2.png"), model), CancellationToken.None));
        error.Message.ShouldContain("cannot make enough memory available");
        always.Runs.Count.ShouldBe(4);
        always.Runs[2].ShouldContain("diffusion=disk,te=cpu,vae=cpu");
        always.Runs[3].ShouldContain("te=cpu");
        always.Runs[3].ShouldContain("--backend");
        var diffusion = store.File(LocalImageModels.Flux2Klein, LocalImageModels.Flux2Klein.Diffusion);
        always.Runs.ShouldAllBe(r => r.Contains(diffusion));
    }

    [Fact]
    public async Task AModelThatFitsNeitherOnTheCardNorInMainMemoryRunsFromTheDisk()
    {
        using var paths = new TempPaths();
        var installer = Installer(paths, "win-x64");
        Install(installer, ToolKind.ImageRuntimeVulkan);
        var store = new LocalImageStore(paths, new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance), NullLogger<LocalImageStore>.Instance);
        Weights(paths, LocalImageModels.QwenImage);
        var selector = new ImageBackendSelector(new FakeGpu(new GpuReport([Amd], false, true)), new FakeSetting { Value = ImageAcceleration.Vulkan }, installer);
        var runner = new PictureRunner(Png(8, 8, (x, y) => [(byte)(x * 30), (byte)(y * 30), 7]));
        // 16 GB card, 13 GiB free (the 11.9 GB diffusion model and its working memory do not fit), 3 GiB of main memory free: the disk level,
        // no question (0.1.17 refused it), and a yellow line says it is slower.
        var notices = new RecordingNotices();
        var engine = Engine(installer, store, selector, new FakeProbe(), runner, mainMemory: 3 * GiB, notices: notices);
        var model = new ResolvedModel(new CatalogModel { Provider = ModelProviders.Local, Id = LocalImageModels.QwenImage.Id }, new ApiKey(), string.Empty);

        await engine.GenerateAsync(new ImageRequest("a map", 512, 512, Path.Combine(paths.WorkDirectory, "1.png"), model), CancellationToken.None);

        runner.Runs.Count.ShouldBe(1);
        runner.Runs[0].ShouldContain("diffusion=disk,te=cpu,vae=cpu");
        notices.Placed.ShouldBe([MemoryPlacement.Disk]);
    }

    [Fact]
    public async Task TooLittleGraphicsMemoryEvenForTheDiskWaitsAndAsksAndRetryRuns()
    {
        using var paths = new TempPaths();
        var installer = Installer(paths, "win-x64");
        Install(installer, ToolKind.ImageRuntimeVulkan);
        var store = new LocalImageStore(paths, new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance), NullLogger<LocalImageStore>.Instance);
        Weights(paths, LocalImageModels.ZImageTurbo);
        var selector = new ImageBackendSelector(new FakeGpu(new GpuReport([Amd], false, true)), new FakeSetting { Value = ImageAcceleration.Vulkan }, installer);
        var runner = new PictureRunner(Png(8, 8, (x, y) => [(byte)(x * 30), (byte)(y * 30), 7]));
        var model = new ResolvedModel(new CatalogModel { Provider = ModelProviders.Local, Id = LocalImageModels.ZImageTurbo.Id, Name = "Z-Image Turbo" },
            new ApiKey(), string.Empty);

        // A game holds the 6 GB card (1.5 GiB free: 0.5 GiB after the reserve); the user closes it and presses Retry: 4.3 GiB free.
        var low = new VramReading((long)(5.9 * GiB), (long)(1.5 * GiB), true, "PDH");
        var freed = new VramReading((long)(5.9 * GiB), (long)(4.3 * GiB), true, "PDH");
        var handler = new Core.ScriptedShortfallHandler(MemoryShortfallChoice.Retry);
        var engine = Engine(installer, store, selector, new FakeProbe(), runner, mainMemory: (long)(2.2 * GiB), vram: new Core.FakeVramProbe(low, low, freed),
            waiter: Core.TestWaiters.Fast(handler));

        await engine.GenerateAsync(new ImageRequest("a map", 512, 512, Path.Combine(paths.WorkDirectory, "1.png"), model), CancellationToken.None);

        handler.Asked.Count.ShouldBe(1);
        handler.Asked[0].CardFreeBytes.ShouldBe((long)(1.5 * GiB));
        handler.Asked[0].CardTotalBytes.ShouldBe((long)(5.9 * GiB));
        handler.Asked[0].Source.ShouldBe("PDH");
        handler.Asked[0].FreeMainBytes.ShouldBe((long)(2.2 * GiB));
        // The developer's laptop after Retry: Z-Image Q8 runs from the disk (2.2 GiB of main memory is too little for its weights), never refused.
        runner.Runs.Count.ShouldBe(1);
        runner.Runs[0].ShouldContain("diffusion=disk,te=cpu,vae=cpu");

        // Nobody to ask (night mode): the same shortfall fails the picture with the figures.
        using (MemoryShortfallScope.Begin(() => true))
        {
            var night = Engine(installer, store, selector, new FakeProbe(), runner, mainMemory: (long)(2.2 * GiB), vram: new Core.FakeVramProbe(low),
                waiter: Core.TestWaiters.Fast(handler));
            var error = await Should.ThrowAsync<MemoryShortfallException>(() =>
                night.GenerateAsync(new ImageRequest("a map", 512, 512, Path.Combine(paths.WorkDirectory, "2.png"), model), CancellationToken.None));
            error.Message.ShouldContain("Z-Image Turbo");
            handler.Asked.Count.ShouldBe(1, "night mode asks nobody");
        }
    }

    [Fact]
    public void ACardOfALowerClassAndABuildTooOldAreFoundBeforeAnythingIsPaid()
    {
        using var paths = new TempPaths();
        var installer = Installer(paths, "win-x64");
        var store = new LocalImageStore(paths, new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance), NullLogger<LocalImageStore>.Instance);
        var selector = new ImageBackendSelector(new FakeGpu(new GpuReport([Amd], false, true)), new FakeSetting(), installer);
        var engine = Engine(installer, store, selector, new FakeProbe(), new PictureRunner([]));
        ResolvedModel Model(LocalImageModel m) => new(new CatalogModel { Provider = ModelProviders.Local, Id = m.Id }, new ApiKey(), string.Empty);

        // A 16 GB card runs every model up to its class; the 32 GB Qwen-Image Q8 is above it.
        engine.Problem(Model(LocalImageModels.Flux3KleinQ8)).ShouldBeNull();
        var problem = engine.Problem(Model(LocalImageModels.QwenImageQ8)).ShouldNotBeNull();
        problem.Code.ShouldBe(ImageEngineProblems.Images);
        problem.Detail.ShouldContain("32 GB class");

        // FLUX.3 needs build 968: an older release is named, an unknown one is not held against it.
        SdCppImageEngine.TooOld(LocalImageModels.Flux3Klein, "master-890-74988b2").ShouldNotBeNull().ShouldContain("968");
        SdCppImageEngine.TooOld(LocalImageModels.Flux3Klein, ToolManifest.SdCppTag).ShouldBeNull();
        SdCppImageEngine.TooOld(LocalImageModels.Flux3Klein, ToolManifest.SdCppLinuxTag).ShouldBeNull();
        SdCppImageEngine.TooOld(LocalImageModels.Flux3Klein, null).ShouldBeNull();
        SdCppImageEngine.TooOld(LocalImageModels.ZImageTurbo, "master-890-74988b2").ShouldBeNull();
        SdCppImageEngine.BuildNumber("master-1106-532d2c1").ShouldBe(1106);
        SdCppImageEngine.BuildNumber("v1").ShouldBe(0);

        // The processor build or an unknown card size never blocks.
        SdCppImageEngine.TooSmall(LocalImageModels.QwenImageQ8, new ImageBackendChoice(ImageBackend.Cpu, ImageAcceleration.Cpu, null)).ShouldBeNull();
        SdCppImageEngine.TooSmall(LocalImageModels.QwenImageQ8, new ImageBackendChoice(ImageBackend.Vulkan, ImageAcceleration.Automatic,
            new GpuAdapter(GpuVendor.Amd, "card", null))).ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ASingleColourPictureIsFoundWhateverTheRowFilter(byte filter)
    {
        PngImage.IsSingleColour(Png(16, 9, (_, _) => [12, 200, 34], filter)).ShouldBeTrue();
        PngImage.IsSingleColour(Png(16, 9, (x, y) => [12, (byte)(x == 15 && y == 8 ? 201 : 200), 34], filter)).ShouldBeFalse();
    }

    [Fact]
    public void WhatIsNotAReadablePngIsNeverCalledBlank()
    {
        PngImage.IsSingleColour([1, 2, 3]).ShouldBeFalse();
        var png = Png(4, 4, (_, _) => [0, 0, 0]);
        PngImage.IsSingleColour(png.AsSpan(0, png.Length / 2)).ShouldBeFalse();
    }

    private static (GpuReport Report, string Rid) Computer(string description)
    {
        var parts = description.Split(' ');
        var flags = parts[0].Split('+');
        var adapters = new List<GpuAdapter>();
        if (flags.Contains("nvidia"))
        {
            adapters.Add(Nvidia);
        }

        if (flags.Contains("intel"))
        {
            adapters.Add(Intel);
        }

        if (flags.Contains("amd"))
        {
            adapters.Add(Amd);
        }

        if (flags.Contains("other"))
        {
            adapters.Add(Virtual);
        }

        return (new GpuReport(adapters, flags.Contains("cuda"), flags.Contains("vulkan")), parts[1] == "win" ? "win-x64" : "linux-x64");
    }

    private static ToolInstaller Installer(TempPaths paths, string rid) =>
        new(paths, new RidPlatform(rid), new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance), NullLogger<ToolInstaller>.Instance);

    private static (ImageRuntimeComponent Runtime, ImageModelComponent Model) Components(TempPaths paths, ToolInstaller installer, ImageBackend backend)
    {
        var setting = new FakeSetting
        {
            Value = backend switch
            {
                ImageBackend.Cuda => ImageAcceleration.Cuda,
                ImageBackend.Vulkan => ImageAcceleration.Vulkan,
                _ => ImageAcceleration.Cpu,
            },
        };
        var runtime = new ImageRuntimeComponent(installer, new ImageBackendSelector(new FakeGpu(GpuReport.None), setting, installer));
        var store = new LocalImageStore(paths, new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance), NullLogger<LocalImageStore>.Instance);
        var model = new ImageModelComponent(LocalImageModels.Flux2Klein, store, runtime);
        model.DependsOn.ShouldBe([ComponentCodes.ImageRuntime]);
        return (runtime, model);
    }

    /// <summary>Puts a tool's files where the installer looks (its executable and the marker), as an install would.</summary>
    private static void Install(ToolInstaller installer, ToolKind tool)
    {
        var archive = installer.Archive(tool).ShouldNotBeNull();
        var folder = installer.InstallFolder(tool);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, archive.ExecutablePath), "fake");
        File.WriteAllText(Path.Combine(folder, ".installed"), "{}");
    }

    private static void Weights(TempPaths paths, LocalImageModel model)
    {
        var folder = Path.Combine(paths.ToolsDirectory, "images", model.Id);
        Directory.CreateDirectory(folder);
        foreach (var file in model.Files)
        {
            File.WriteAllText(Path.Combine(folder, file.ExecutablePath), "weights");
        }
    }

    /// <summary>An 8-bit RGB PNG whose rows are written with <paramref name="filter"/> (the reader must undo it).</summary>
    private static byte[] Png(int width, int height, Func<int, int, byte[]> pixel, byte filter = 0)
    {
        var stride = width * 3;
        var raw = new byte[(stride + 1) * height];
        var previous = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var row = new byte[stride];
            for (var x = 0; x < width; x++)
            {
                pixel(x, y).CopyTo(row, x * 3);
            }

            raw[y * (stride + 1)] = filter;
            for (var i = 0; i < stride; i++)
            {
                int left = i >= 3 ? row[i - 3] : 0;
                int up = y > 0 ? previous[i] : 0;
                int corner = i >= 3 && y > 0 ? previous[i - 3] : 0;
                var predicted = filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) >> 1,
                    4 => Paeth(left, up, corner),
                    _ => 0,
                };
                raw[y * (stride + 1) + 1 + i] = (byte)(row[i] - predicted);
            }

            previous = row;
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void Chunk(MemoryStream png, string type, byte[] body)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, body.Length);
        png.Write(length);
        png.Write(System.Text.Encoding.ASCII.GetBytes(type));
        png.Write(body);
        png.Write(new byte[4]);
    }

    /// <summary>The engine with a coordinator that grants every lease at once, a 16 GB card with 13 GiB free and 24 GiB of free main memory.</summary>
    private static SdCppImageEngine Engine(ToolInstaller installer, LocalImageStore store, ImageBackendSelector selector,
        TMConcept.Engines.Hardware.IComputeRuntimeProbe probe, PictureRunner runner, VramReading? memory = null, long? mainMemory = 24 * GiB,
        IMemoryNotices? notices = null, IVramProbe? vram = null, IMemoryWaiter? waiter = null)
    {
        var gpu = new FakeCoordinator();
        runner.Coordinator = gpu;
        return new SdCppImageEngine(installer, store, selector, probe, runner, gpu, vram ?? new FakeVram(memory ?? new VramReading(16 * GiB, 13 * GiB, true, "test")),
            new FakeMainMemory(mainMemory), waiter ?? Core.TestWaiters.Fast(), NullLogger<SdCppImageEngine>.Instance, notices);
    }

    /// <summary>Keeps every memory notice.</summary>
    internal sealed class RecordingNotices : IMemoryNotices
    {
        public List<MemoryNotice> Notices { get; } = [];

        public IReadOnlyList<MemoryNoticeKind> Kinds => Notices.Select(n => n.Kind).ToList();

        public IReadOnlyList<MemoryPlacement> Placed => Notices.Where(n => n.Kind == MemoryNoticeKind.Placed).Select(n => n.Placement).ToList();

        public void Report(MemoryNotice notice) => Notices.Add(notice);
    }

    /// <summary>Grants every lease at once and counts the leases held now.</summary>
    internal sealed class FakeCoordinator : IGpuCoordinator
    {
        public int Held { get; private set; }

        public List<string> Leased { get; } = [];

        public ILocalModel? Loaded => null;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public Task<IGpuLease> AcquireAsync(IModel model, ModelMemoryNeed? need, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IGpuLease> AcquireAsync(CatalogModel model, CancellationToken cancellationToken)
        {
            Held++;
            Leased.Add(model.Provider + "/" + model.Id);
            return Task.FromResult<IGpuLease>(new Lease(this));
        }

        public Task FreeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>The names <see cref="RetryStuckAsync"/> answers (models still stuck).</summary>
        public List<string> Stuck { get; } = [];

        /// <summary>How often a memory check asked the stuck models again.</summary>
        public int StuckAsks { get; private set; }

        public Task<IReadOnlyList<string>> RetryStuckAsync(IModel keep, CancellationToken cancellationToken)
        {
            StuckAsks++;
            return Task.FromResult<IReadOnlyList<string>>(Stuck.ToList());
        }

        private sealed class Lease(FakeCoordinator owner) : IGpuLease
        {
            public IModel Model => throw new NotSupportedException();

            public bool OnCard => true;

            public VramReading Memory => VramReading.None;

            public ValueTask DisposeAsync()
            {
                owner.Held--;
                return ValueTask.CompletedTask;
            }
        }
    }

    internal sealed class FakeVram(VramReading reading) : IVramProbe
    {
        public Task<VramReading> MeasureAsync(GpuAdapter? adapter, CancellationToken cancellationToken) => Task.FromResult(reading);
    }

    internal sealed class FakeMainMemory(long? bytes) : ISystemMemory
    {
        public long? AvailableBytes() => bytes;
    }

    private sealed class FakeSetting : IImageAccelerationSetting
    {
        public ImageAcceleration Value { get; set; }
    }

    /// <summary>
    /// "Runs" sd-cli by writing the given picture to its <c>-o</c> path; the program <see cref="FailFor"/> ends with 0xC0000135 and writes
    /// nothing instead.
    /// </summary>
    private sealed class PictureRunner(byte[] picture) : IProcessRunner
    {
        public ProcessStart? Start { get; private set; }

        public string? FailFor { get; init; }

        /// <summary>The first this many runs end out of memory (sd-cli's own message) and write nothing.</summary>
        public int OutOfMemoryRuns { get; set; }

        public List<string> Programs { get; } = [];

        public List<IReadOnlyList<string>> Runs { get; } = [];

        public List<bool> InsideLease { get; } = [];

        public FakeCoordinator? Coordinator { get; set; }

        public Task<ProcessResult> RunAsync(ProcessStart start, Action<string>? onLine, CancellationToken cancellationToken)
        {
            Start = start;
            Programs.Add(start.Program);
            Runs.Add(start.Arguments);
            InsideLease.Add(Coordinator is { Held: > 0 });
            if (start.Program == FailFor)
            {
                return Task.FromResult(new ProcessResult(SdCppImageEngine.DllNotFound, string.Empty));
            }

            if (OutOfMemoryRuns > 0)
            {
                OutOfMemoryRuns--;
                return Task.FromResult(new ProcessResult(1,
                    "[ERROR] model manager cannot make enough memory available on Vulkan0: need 3666.57 MB device / 3154.57 MB budget"));
            }

            var output = start.Arguments[start.Arguments.ToList().IndexOf("-o") + 1];
            File.WriteAllBytes(output, picture);
            return Task.FromResult(new ProcessResult(0, string.Empty));
        }
    }

    private sealed class FakeProbe : TMConcept.Engines.Hardware.IComputeRuntimeProbe
    {
        public IReadOnlyList<string> Missing { get; init; } = [];

        public bool CudaDriver() => false;

        public bool Vulkan { get; init; } = true;

        public bool VulkanLoader() => Vulkan;

        public IReadOnlyList<string> MissingVisualCppRuntime() => Missing;
    }

    private sealed class RidPlatform(string rid) : IToolPlatform
    {
        public string Rid => rid;

        public bool UseSystemPrograms => false;

        public string Executable(string name) => rid.StartsWith("win", StringComparison.Ordinal) ? name + ".exe" : name;

        public Task ExtractAsync(string archive, string targetFolder, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> MissingLibrariesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
