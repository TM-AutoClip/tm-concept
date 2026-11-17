using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Components;
using TMConcept.Core.Hardware;
using TMConcept.Core.Tools;
using TMConcept.Engines.AppOllama;
using TMConcept.Engines.Tools;
using TMConcept.Tests.Storage;
using ZstdSharp;

namespace TMConcept.Tests.Engines;

/// <summary>
/// The app's own Ollama program (0.1.20): the pinned release, the SHA-256 check before anything is unpacked, the graphics backends this
/// computer does not need deleted after unpacking, and the Linux <c>.tar.zst</c> read with the managed zstd decoder. A fake network serves
/// small packages: the real 1.6 GB release is never downloaded.
/// </summary>
public sealed class AppOllamaInstallTests
{
    private static readonly GpuReport Amd = new([new GpuAdapter(GpuVendor.Amd, "Radeon RX 6600", 8L << 30)], false, true);

    private static GpuReport Nvidia(int driver, int capability = 0) =>
        new([new GpuAdapter(GpuVendor.Nvidia, "GeForce RTX 3060", 6L << 30)], true, true) { Cuda = new CudaDetails(driver, capability) };

    private static byte[] WindowsPackage()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[]
            {
                "ollama.exe", "lib/ollama/ggml-base.dll", "lib/ollama/ggml-cpu-haswell.dll", "lib/ollama/cuda_v12/ggml-cuda.dll",
                "lib/ollama/cuda_v13/ggml-cuda.dll", "lib/ollama/vulkan/ggml-vulkan.dll",
            })
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write("fake " + name);
            }
        }

        return memory.ToArray();
    }

    private static (AppOllamaComponent Component, Downloads Network, TempPaths Paths) Create(byte[] body, GpuReport report, string? sha256 = null)
    {
        var paths = new TempPaths();
        var network = new Downloads(body);
        var archive = AppOllamaRelease.Archives.First(a => a.Rid == "win-x64") with { Sha256 = sha256 ?? Convert.ToHexStringLower(SHA256.HashData(body)) };
        var component = new AppOllamaComponent(paths, new Platform("win-x64"), new ToolDownloader(new DownloadTransport(() => network), NullLogger<ToolDownloader>.Instance),
            new Gpu(report), new AppOllamaLayout(Path.Combine(paths.ToolsDirectory, "ollama-models"), Path.Combine(paths.ToolsDirectory, "ollama-home")),
            NullLogger<AppOllamaComponent>.Instance)
        {
            Archives = [archive],
        };
        return (component, network, paths);
    }

    [Fact]
    public void ThePinnedReleaseIsTheOfficialPackageWithItsDigest()
    {
        AppOllamaRelease.Archives.Select(a => a.Rid).ShouldBe(["win-x64", "linux-x64", "linux-arm64"]);
        foreach (var archive in AppOllamaRelease.Archives)
        {
            archive.Tool.ShouldBe(ToolKind.AppOllama);
            archive.Url.Host.ShouldBe("github.com");
            archive.Url.AbsolutePath.ShouldStartWith("/ollama/ollama/releases/download/v" + AppOllamaRelease.Version + "/");
            archive.Sha256.Length.ShouldBe(64);
            archive.Sha256.ShouldAllBe(c => char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c));
            archive.ApproximateBytes.ShouldBeGreaterThan(1_000_000_000);
        }

        AppOllamaRelease.Archives[0].ExecutablePath.ShouldBe("ollama.exe");
        AppOllamaRelease.Archives[1].Url.AbsolutePath.ShouldEndWith(".tar.zst");
        AppOllamaRelease.Archives[1].ExecutablePath.ShouldBe("bin/ollama");
    }

    [Fact]
    public void OnlyTheBackendThisCardNeedsIsKept()
    {
        AppOllamaBackends.Keep(Nvidia(13000, 89)).ShouldBe([AppOllamaBackends.Cuda13, AppOllamaBackends.Vulkan]);
        AppOllamaBackends.Keep(Nvidia(13000, 61)).ShouldBe([AppOllamaBackends.Cuda12, AppOllamaBackends.Vulkan], "CUDA 13 dropped Pascal cards");
        AppOllamaBackends.Keep(Nvidia(12080)).ShouldBe([AppOllamaBackends.Cuda12, AppOllamaBackends.Vulkan]);
        AppOllamaBackends.Keep(Nvidia(0)).ShouldBe([AppOllamaBackends.Cuda12, AppOllamaBackends.Cuda13, AppOllamaBackends.Vulkan], "an unknown driver keeps both");
        AppOllamaBackends.Keep(Amd).ShouldBe([AppOllamaBackends.Vulkan]);
        AppOllamaBackends.Keep(GpuReport.None).ShouldBeEmpty();
        AppOllamaBackends.IsBackend("rocm_v7_1").ShouldBeTrue();
        AppOllamaBackends.IsBackend("ggml-base.dll").ShouldBeFalse();
    }

    [Fact]
    public void PruningDeletesOnlyUnneededBackendFolders()
    {
        using var paths = new TempPaths();
        var library = Path.Combine(paths.DataDirectory, "lib", "ollama");
        foreach (var folder in new[] { "cuda_v12", "cuda_v13", "vulkan", "rocm_v7_1", "something_new" })
        {
            Directory.CreateDirectory(Path.Combine(library, folder));
            File.WriteAllText(Path.Combine(library, folder, "x.dll"), "x");
        }

        File.WriteAllText(Path.Combine(library, "ggml-base.dll"), "x");

        AppOllamaComponent.Prune(library, Amd).ShouldBe(["cuda_v12", "cuda_v13", "rocm_v7_1"]);

        Directory.EnumerateDirectories(library).Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(["something_new", "vulkan"]);
        File.Exists(Path.Combine(library, "ggml-base.dll")).ShouldBeTrue("the processor libraries always stay");
    }

    [Fact]
    public async Task AnInstallChecksTheHashUnpacksAndKeepsOnlyTheNeededBackend()
    {
        var (component, network, paths) = Create(WindowsPackage(), Nvidia(12080, 86));
        using (paths)
        {
            component.Ready.ShouldBeFalse();
            component.Downloads().ShouldHaveSingleItem().Host.ShouldBe("github.com");
            network.Requests.ShouldBeEmpty("looking at a component never downloads");

            await component.InstallAsync(null, CancellationToken.None);

            component.Ready.ShouldBeTrue();
            ((IAppOllamaRuntime)component).Executable.ShouldBe(Path.Combine(paths.ToolsDirectory, "ollama", AppOllamaRelease.Version, "ollama.exe"));
            var library = Path.Combine(component.InstallFolder, "lib", "ollama");
            Directory.EnumerateDirectories(library).Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(["cuda_v12", "vulkan"]);
            File.ReadAllText(Path.Combine(component.InstallFolder, ".installed")).ShouldContain("cuda_v13");
            Directory.EnumerateFiles(Path.Combine(paths.ToolsDirectory, "downloads")).ShouldBeEmpty("the package is deleted after the install");
            component.Folders().ShouldBe([component.InstallFolder]);
            component.Downloads().ShouldBeEmpty();
            component.WasInstalled.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task APackageWhoseHashDoesNotMatchIsNeverUnpacked()
    {
        var (component, _, paths) = Create(WindowsPackage(), Amd, sha256: new string('0', 64));
        using (paths)
        {
            var error = await Should.ThrowAsync<ToolInstallException>(() => component.InstallAsync(null, CancellationToken.None));

            error.Code.ShouldBe("hash");
            component.Ready.ShouldBeFalse();
            Directory.Exists(component.InstallFolder).ShouldBeFalse();
            Directory.Exists(component.InstallFolder + ".unpack").ShouldBeFalse();
        }
    }

    [Fact]
    public async Task ALinuxTarZstIsUnpackedWithTheManagedDecoder()
    {
        using var paths = new TempPaths();
        var archive = Path.Combine(paths.DataDirectory, "ollama-linux-amd64.tar.zst");
        Write(archive, ("bin/ollama", "fake program"), ("lib/ollama/libggml-base.so", "x"), ("lib/ollama/cuda_v13/libggml-cuda.so", "y"));
        var target = Path.Combine(paths.DataDirectory, "unpacked");

        await AppOllamaComponent.ExtractAsync(archive, target, CancellationToken.None);

        File.ReadAllText(Path.Combine(target, "bin", "ollama")).ShouldBe("fake program");
        File.Exists(Path.Combine(target, "lib", "ollama", "cuda_v13", "libggml-cuda.so")).ShouldBeTrue();

        var evil = Path.Combine(paths.DataDirectory, "evil.tar.zst");
        Write(evil, ("../outside.txt", "no"));
        await Should.ThrowAsync<InvalidDataException>(() => AppOllamaComponent.ExtractAsync(evil, Path.Combine(paths.DataDirectory, "evil"), CancellationToken.None));
        File.Exists(Path.Combine(paths.DataDirectory, "outside.txt")).ShouldBeFalse();
    }

    [Fact]
    public void TheModelsOfTheAppsOllamaAreComponentsThatNeedIt()
    {
        var models = new AppModelComponentSource(new AppModelStore(new AppOllamaLayout("m", "h")),
            new AppModelRegistry(new AppModelStore(new AppOllamaLayout("m", "h")), NullLogger<AppModelRegistry>.Instance), new Core.FakeAppOllamaRuntime(),
            new Providers.FakeRunningAppServer("http://127.0.0.1:11534"), new Core.FakeAppOllamaApi(), NullLoggerFactory.Instance);

        models.Components.Count.ShouldBe(AppModelCatalog.Models.Count);
        models.Components.ShouldAllBe(c => c.DependsOn.Single() == ComponentCodes.AppOllama);
        models.Components.ShouldAllBe(c => c.Code.StartsWith(ComponentCodes.AppOllamaModelPrefix, StringComparison.Ordinal));
        TMConcept.Core.DiskSpace.ComponentDiskItem.AreaOf(models.Components[0].Code).ShouldBe(TMConcept.Core.DiskSpace.DiskArea.Models);
        TMConcept.Core.DiskSpace.ComponentDiskItem.AreaOf(ComponentCodes.AppOllama).ShouldBe(TMConcept.Core.DiskSpace.DiskArea.Tools);
    }

    private static void Write(string archive, params (string Name, string Text)[] files)
    {
        using var output = File.Create(archive);
        using var zstd = new CompressionStream(output);
        using (var tar = new TarWriter(zstd, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) };
                tar.WriteEntry(entry);
            }
        }
    }

    private sealed class Gpu(GpuReport report) : IGpuInfo
    {
        public GpuReport Detect() => report;
    }

    private sealed class Platform(string rid) : IToolPlatform
    {
        public string Rid => rid;

        public bool UseSystemPrograms => false;

        public string Executable(string name) => name + ".exe";

        public Task ExtractAsync(string archive, string targetFolder, CancellationToken cancellationToken) => throw new NotSupportedException("not used");

        public Task<IReadOnlyList<string>> MissingLibrariesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class Downloads(byte[] body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }
}
