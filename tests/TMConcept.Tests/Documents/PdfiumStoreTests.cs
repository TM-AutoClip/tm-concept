using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Documents;
using TMConcept.Core.Tools;
using TMConcept.Documents.Pdf;
using TMConcept.Engines.Tools;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Documents;

/// <summary>
/// 0.1.27, PDFium as a component: the pins (one build per supported computer, SHA-256, an allowed host), the unpacking in the app (only the
/// library and the licence texts are taken, a name that would leave the folder stops it), the install (the marker last) and the page size
/// arithmetic. Archives made by the test with TarWriter; nothing is downloaded and no PDFium library is loaded.
/// </summary>
public sealed class PdfiumStoreTests : IDisposable
{
    private readonly TempPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    [Fact]
    public void ThePinsCoverWindowsAndLinuxFromAnAllowedHost()
    {
        PdfiumRelease.Archives.Select(a => a.Rid).ShouldBe(["win-x64", "linux-x64", "linux-arm64"]);
        foreach (var archive in PdfiumRelease.Archives)
        {
            archive.Tool.ShouldBe(ToolKind.PdfRenderer);
            archive.Sha256.Length.ShouldBe(64);
            archive.Sha256.ShouldAllBe(c => Uri.IsHexDigit(c));
            archive.ApproximateBytes.ShouldBeGreaterThan(1_000_000);
            ToolManifest.AllowedHosts.ShouldContain(archive.Url.Host);
            archive.Url.AbsoluteUri.ShouldContain("chromium%2F8138");
        }
    }

    [Fact]
    public void OnlyTheLibraryAndTheLicencesAreUnpacked()
    {
        var tgz = Archive(("LICENSE", "MIT"), ("VERSION", "MAJOR=159"), ("bin/pdfium.dll", "library"), ("include/fpdfview.h", "header"),
            ("licenses/pdfium.txt", "BSD"), ("licenses/libjpeg_turbo.ijg", "IJG"), ("licenses/deep/x.txt", "nested"), ("lib/pdfium.dll.lib", "import"));
        var folder = Path.Combine(_paths.ToolsDirectory, "unpacked");

        var written = PdfiumStore.Unpack(new MemoryStream(tgz), folder, "bin/pdfium.dll");

        written.ShouldBe(["LICENSE", "pdfium.dll", "licenses/pdfium.txt", "licenses/libjpeg_turbo.ijg"]);
        File.ReadAllText(Path.Combine(folder, "pdfium.dll")).ShouldBe("library");
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length.ShouldBe(4);
    }

    [Fact]
    public void ANameThatWouldLeaveTheFolderStopsTheUnpacking()
    {
        var folder = Path.Combine(_paths.ToolsDirectory, "unpacked");
        var outside = Path.Combine(_paths.ToolsDirectory, "evil.txt");

        Should.Throw<InvalidDataException>(() =>
            PdfiumStore.Unpack(new MemoryStream(Archive(("bin/pdfium.dll", "library"), ("../evil.txt", "x"))), folder, "bin/pdfium.dll"));
        Should.Throw<InvalidDataException>(() =>
            PdfiumStore.Unpack(new MemoryStream(Archive(("licenses/../../evil.txt", "x"))), folder, "bin/pdfium.dll"));
        Should.Throw<InvalidDataException>(() => PdfiumStore.Unpack(new MemoryStream(Archive(("LICENSE", "MIT"))), folder, "bin/pdfium.dll"),
            "an archive without the library");
        File.Exists(outside).ShouldBeFalse();
    }

    [Fact]
    public async Task TheInstallUnpacksTheVerifiedArchiveAndWritesTheMarkerLast()
    {
        var tgz = Archive(("LICENSE", "MIT"), ("bin/pdfium.dll", "library"), ("licenses/pdfium.txt", "BSD"));
        var downloads = new ArchiveWriter(tgz);
        var store = new PdfiumStore(_paths, new Platform("win-x64"), downloads, NullLogger<PdfiumStore>.Instance);
        var component = new PdfiumComponentSource(store).Components.ShouldHaveSingleItem();
        component.Code.ShouldBe(PageTextEngines.PdfRendererComponent);
        component.Supported.ShouldBeTrue();
        component.Ready.ShouldBeFalse();
        component.Downloads().ShouldHaveSingleItem().Host.ShouldBe("github.com");

        await component.InstallAsync(null, CancellationToken.None);

        component.Ready.ShouldBeTrue();
        downloads.Asked.ShouldBe([PdfiumRelease.Archives[0]]);
        store.LibraryFile.ShouldBe(Path.Combine(_paths.ToolsDirectory, "pdfium", PdfiumRelease.Version, "pdfium.dll"));
        File.Exists(Path.Combine(store.Folder, "licenses", "pdfium.txt")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(store.Folder, PdfiumStore.Marker)).ShouldBe(PdfiumRelease.Version);
        Directory.GetFiles(Path.GetDirectoryName(store.Folder)!).ShouldBeEmpty("the archive is deleted after unpacking");
        component.Downloads().ShouldBeEmpty();
        new PdfiumRenderer(store).Ready.ShouldBeTrue();
    }

    [Fact]
    public void AComputerWithoutABuildIsNotSupportedAndTheRendererIsNotReady()
    {
        var store = new PdfiumStore(_paths, new Platform("osx-arm64"), new ArchiveWriter([]), NullLogger<PdfiumStore>.Instance);
        store.Supported.ShouldBeFalse();
        store.Ready.ShouldBeFalse();
        var renderer = new PdfiumRenderer(store);
        renderer.Ready.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => renderer.Open("any.pdf"));
    }

    [Theory]
    [InlineData(595.0, 842.0, 200, 1652, 2338)]
    [InlineData(842.0, 595.0, 72, 842, 595)]
    [InlineData(2384.0, 3370.0, 200, 3364, 4755)]
    public void APageIsDrawnAtTheResolutionUnlessItIsTooLarge(double width, double height, int dpi, int pixelsWide, int pixelsHigh) =>
        PdfiumRenderer.Size(width, height, dpi, 16_000_000, PdfiumRenderer.SideMax).ShouldBe((pixelsWide, pixelsHigh));

    [Fact]
    public void APageWithoutASizeCannotBeDrawn()
    {
        Should.Throw<InvalidDataException>(() => PdfiumRenderer.Size(0, 842, 200, 16_000_000, 30_000));
        Should.Throw<InvalidDataException>(() => PdfiumRenderer.Size(double.NaN, 842, 200, 16_000_000, 30_000));
        PdfiumRenderer.Size(1, 100_000, 200, long.MaxValue, 30_000).Height.ShouldBe(30_000);
    }

    private static byte[] Archive(params (string Name, string Text)[] files)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)) };
                tar.WriteEntry(entry);
            }
        }

        return output.ToArray();
    }

    /// <summary>"Downloads" a given archive (as if its SHA-256 had matched) and records what was asked.</summary>
    private sealed class ArchiveWriter(byte[] archive) : IArchiveDownloader
    {
        public List<ToolArchive> Asked { get; } = [];

        public Task DownloadAsync(ToolArchive pinned, string file, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
        {
            Asked.Add(pinned);
            File.WriteAllBytes(file, archive);
            return Task.CompletedTask;
        }
    }

    private sealed class Platform(string rid) : IToolPlatform
    {
        public string Rid => rid;

        public bool UseSystemPrograms => false;

        public string Executable(string name) => name;

        public Task ExtractAsync(string archive, string targetFolder, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> MissingLibrariesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
