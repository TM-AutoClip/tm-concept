using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Tools;
using TMConcept.Engines.Processes;
using TMConcept.Engines.Tools;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Engines;

public sealed class ToolInstallTests
{
    private static (ToolInstaller Installer, FakeDownloads Fake, TempPaths Paths) Create(byte[] body, Func<ToolArchive, ToolArchive>? edit = null)
    {
        var paths = new TempPaths();
        var fake = new FakeDownloads(body);
        var archive = edit?.Invoke(Sample(body)) ?? Sample(body);
        var downloader = new ToolDownloader(new DownloadTransport(() => fake), NullLogger<ToolDownloader>.Instance);
        var installer = new ToolInstaller(paths, new FakePlatform(), downloader, NullLogger<ToolInstaller>.Instance) { Archives = [archive] };
        return (installer, fake, paths);
    }

    private static ToolInstaller Installer(TempPaths paths, FakeDownloads fake, ToolKind tool, byte[] body, string searchPath) =>
        new(paths, new FakePlatform(), new ToolDownloader(new DownloadTransport(() => fake), NullLogger<ToolDownloader>.Instance),
            NullLogger<ToolInstaller>.Instance)
        {
            Archives = [ToolManifest.Archives.First(a => a.Tool == tool && a.Rid == "win-x64") with
            {
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(body)),
                Root = tool == ToolKind.Ffmpeg ? "ffmpeg-root" : "node-v26.13.0-win-x64",
            }],
            SearchPath = searchPath,
        };

    private static ToolArchive Sample(byte[] body) =>
        ToolManifest.Archives.First(a => a.Tool == ToolKind.Node && a.Rid == "win-x64") with { Sha256 = Convert.ToHexStringLower(SHA256.HashData(body)) };

    private static byte[] Zip(string root, string exe)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry(root + "/" + exe).Open());
            writer.Write("fake node");
        }

        return memory.ToArray();
    }

    [Fact]
    public async Task AnArchiveWhoseHashDoesNotMatchIsRejectedAndDeleted()
    {
        var body = Zip("node-v26.13.0-win-x64", "node.exe");
        var paths = new TempPaths();
        var fake = new FakeDownloads(body);
        var downloader = new ToolDownloader(new DownloadTransport(() => fake), NullLogger<ToolDownloader>.Instance);
        var archive = Sample(body) with { Sha256 = new string('0', 64) };
        var file = Path.Combine(paths.DataDirectory, "node.zip");

        var error = await Should.ThrowAsync<ToolInstallException>(() => downloader.DownloadAsync(archive, file, null, CancellationToken.None));

        error.Code.ShouldBe("hash");
        File.Exists(file).ShouldBeFalse();
        File.Exists(file + ".part").ShouldBeFalse();
        paths.Dispose();
    }

    [Fact]
    public async Task TwoInstallsOfTheSameToolAtOnceDownloadOnceAndBothEndReady()
    {
        var body = Zip("node-v26.13.0-win-x64", "node.exe");
        var (installer, fake, paths) = Create(body);

        var both = await Task.WhenAll(
            Task.Run(() => installer.InstallAsync(ToolKind.Node, agreed: true, null, CancellationToken.None)),
            Task.Run(() => installer.InstallAsync(ToolKind.Node, agreed: true, null, CancellationToken.None)));

        both.ShouldAllBe(s => s.Ready);
        fake.Requests.Count.ShouldBe(1);
        Directory.GetDirectories(paths.ToolsDirectory, "*.unpack", SearchOption.AllDirectories).ShouldBeEmpty();
        paths.Dispose();
    }

    [Fact]
    public async Task AMatchingArchiveIsUnpackedAndMarkedReady()
    {
        var body = Zip("node-v26.13.0-win-x64", "node.exe");
        var (installer, fake, paths) = Create(body);
        installer.Status(ToolKind.Node).Ready.ShouldBeFalse();

        var status = await installer.InstallAsync(ToolKind.Node, agreed: true, null, CancellationToken.None);

        status.Ready.ShouldBeTrue();
        status.ExecutablePath.ShouldStartWith(paths.ToolsDirectory);
        fake.Requests.Single().Host.ShouldBe("nodejs.org");
        Directory.GetFiles(Path.Combine(paths.ToolsDirectory, "downloads")).ShouldBeEmpty();
        paths.Dispose();
    }

    [Fact]
    public async Task NothingIsInstalledWithoutConsentOrFromAnotherHost()
    {
        var body = Zip("node-v26.13.0-win-x64", "node.exe");
        var (installer, fake, paths) = Create(body);
        await Should.ThrowAsync<InvalidOperationException>(() => installer.InstallAsync(ToolKind.Node, agreed: false, null, CancellationToken.None));
        fake.Requests.ShouldBeEmpty();

        fake.RedirectTo = new Uri("https://example.com/evil.zip");
        var error = await Should.ThrowAsync<ToolInstallException>(() => installer.InstallAsync(ToolKind.Node, agreed: true, null, CancellationToken.None));
        error.Code.ShouldBe("host");
        paths.Dispose();
    }

    [Fact]
    public async Task AGoneFfmpegDownloadFallsBackToTheCopyOnThePath()
    {
        var body = Zip("ffmpeg-root", "bin/ffmpeg.exe");
        using var paths = new TempPaths();
        var system = Path.Combine(paths.DataDirectory, "system-bin");
        Directory.CreateDirectory(system);
        await File.WriteAllTextAsync(Path.Combine(system, "ffmpeg.exe"), "x", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(system, "ffprobe.exe"), "x", CancellationToken.None);
        var fake = new FakeDownloads(body) { Status = HttpStatusCode.NotFound };
        var installer = Installer(paths, fake, ToolKind.Ffmpeg, body, system);

        var status = await installer.InstallAsync(ToolKind.Ffmpeg, agreed: true, null, CancellationToken.None);

        status.Ready.ShouldBeTrue();
        status.Source.ShouldBe(ToolInstaller.FallbackSource);
        installer.Status(ToolKind.Ffmpeg).ExecutablePath.ShouldBe(Path.Combine(system, "ffmpeg.exe"));
        Directory.Exists(Path.Combine(paths.ToolsDirectory, "ffmpeg", "8.1.3")).ShouldBeFalse("nothing was unpacked");
    }

    [Fact]
    public async Task AWrongFfmpegDownloadWithoutASystemCopyFailsClearlyAndRunsNothing()
    {
        var body = Zip("ffmpeg-root", "bin/ffmpeg.exe");
        using var paths = new TempPaths();
        var fake = new FakeDownloads(body);
        var installer = Installer(paths, fake, ToolKind.Ffmpeg, body, Path.Combine(paths.DataDirectory, "empty"));
        var wrong = new ToolInstaller(paths, new FakePlatform(), new ToolDownloader(new DownloadTransport(() => fake), NullLogger<ToolDownloader>.Instance),
            NullLogger<ToolInstaller>.Instance)
        {
            Archives = [installer.Archives[0] with { Sha256 = new string('0', 64) }],
            SearchPath = Path.Combine(paths.DataDirectory, "empty"),
        };

        var error = await Should.ThrowAsync<ToolInstallException>(() => wrong.InstallAsync(ToolKind.Ffmpeg, agreed: true, null, CancellationToken.None));

        error.Code.ShouldBe("unavailable");
        wrong.Status(ToolKind.Ffmpeg).Ready.ShouldBeFalse();
        Directory.Exists(Path.Combine(paths.ToolsDirectory, "ffmpeg", "8.1.3")).ShouldBeFalse();
    }

    [Fact]
    public async Task AGoneNodeDownloadHasNoFallback()
    {
        var body = Zip("node-v26.13.0-win-x64", "node.exe");
        using var paths = new TempPaths();
        var system = Path.Combine(paths.DataDirectory, "system-bin");
        Directory.CreateDirectory(system);
        await File.WriteAllTextAsync(Path.Combine(system, "node.exe"), "x", CancellationToken.None);
        var fake = new FakeDownloads(body) { Status = HttpStatusCode.NotFound };
        var installer = Installer(paths, fake, ToolKind.Node, body, system);

        var error = await Should.ThrowAsync<ToolInstallException>(() => installer.InstallAsync(ToolKind.Node, agreed: true, null, CancellationToken.None));

        error.Code.ShouldBe("gone");
        installer.Status(ToolKind.Node).Ready.ShouldBeFalse();
    }

    [Fact]
    public void TheFfmpegPinIsAMonthEndBuild() =>
        ToolManifest.FfmpegTag.ShouldStartWith("autobuild-2026-10-31-", Case.Sensitive, "BtbN keeps a month's last build for two years, a daily build for 14 days");

    [Fact]
    public void EveryPinnedArchiveIsHttpsWithAHashForEveryPlatform()
    {
        foreach (var tool in new[] { ToolKind.Node, ToolKind.Ffmpeg })
        {
            foreach (var rid in new[] { "win-x64", "linux-x64", "linux-arm64" })
            {
                var archive = ToolManifest.Archives.Single(a => a.Tool == tool && a.Rid == rid);
                archive.Url.Scheme.ShouldBe("https");
                archive.Sha256.Length.ShouldBe(64);
                ToolManifest.AllowedHosts.ShouldContain(archive.Url.Host);
                archive.Url.ToString().ShouldNotContain("latest");
            }
        }
    }

    [Fact]
    public void SecretLookingVariablesNeverReachAChildProcess()
    {
        var environment = new Dictionary<string, string?>
        {
            ["PATH"] = "/bin",
            ["ANTHROPIC_API_KEY"] = "x",
            ["GITHUB_TOKEN"] = "x",
            ["DB_PASSWORD"] = "x",
            ["HOME"] = "/home/a",
        };

        ProcessRunner.Clean(environment);

        environment.Keys.ShouldBe(["PATH", "HOME"], ignoreOrder: true);
    }

    private sealed class FakePlatform : IToolPlatform
    {
        public string Rid => "win-x64";

        public bool UseSystemPrograms => false;

        public string Executable(string name) => name + ".exe";

        public Task ExtractAsync(string archive, string targetFolder, CancellationToken cancellationToken)
        {
            ZipFile.ExtractToDirectory(archive, targetFolder);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> MissingLibrariesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class FakeDownloads(byte[] body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        public Uri? RedirectTo { get; set; }

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (RedirectTo is { } next)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = next;
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(Status == HttpStatusCode.OK ? body : []) });
        }
    }
}
