using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Tools;
using TMConcept.Documents.Pdf;
using TMConcept.Tests.Storage;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace TMConcept.Tests.Documents;

/// <summary>
/// 0.1.27: the real PDFium library through the app's own binding, only when a copy is given: <c>TMC_PDFIUM_DIR</c> names a folder with the
/// pinned build's library (<c>pdfium.dll</c> / <c>libpdfium.so</c>). It is copied into a temporary tools folder, never read from or written
/// to the user's data folder. Skipped otherwise (the tests never download anything). Runs on the processor.
/// </summary>
public sealed class PdfiumSmokeTests
{
    [Fact]
    public void ARealPageIsDrawnWithItsTextAndAnUnknownPageOrFileIsRefused()
    {
        var source = Environment.GetEnvironmentVariable("TMC_PDFIUM_DIR");
        var name = OperatingSystem.IsWindows() ? "pdfium.dll" : "libpdfium.so";
        if (string.IsNullOrEmpty(source) || !File.Exists(Path.Combine(source, name)))
        {
            Assert.Skip("TMC_PDFIUM_DIR does not name a folder with " + name + ".");
        }

        // The loaded library cannot be deleted while the test process runs: its folder is kept (under the system's temp folder) and reused.
        using var paths = TempPaths.At(Path.Combine(Path.GetTempPath(), "tmconcept-tests", "pdfium-smoke", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var rid = (OperatingSystem.IsWindows() ? "win-" : "linux-") + (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64");
        var store = new PdfiumStore(paths, new Platform(rid), new NoDownloads(), NullLogger<PdfiumStore>.Instance);
        Directory.CreateDirectory(store.Folder);
        File.Copy(Path.Combine(source, name), store.LibraryFile);
        File.WriteAllText(Path.Combine(store.Folder, PdfiumStore.Marker), PdfiumRelease.Version);

        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("Konstantinopolis", 40, new PdfPoint(60, 700), font);
        Directory.CreateDirectory(paths.DataDirectory);
        var file = Path.Combine(paths.DataDirectory, "ş ü (test).pdf");
        File.WriteAllBytes(file, builder.Build());

        using var document = new PdfiumRenderer(store).Open(file);
        document.Pages.ShouldBe(1);
        var page = document.Render(1, 100, 16_000_000);

        (page.Width, page.Height).ShouldBe((826, 1169), "A4 at 100 dpi");
        page.Bgrx.Length.ShouldBe(page.Width * page.Height * 4);
        var dark = 0;
        for (var i = 0; i < page.Bgrx.Length; i += 4)
        {
            dark += page.Bgrx[i] < 128 && page.Bgrx[i + 1] < 128 && page.Bgrx[i + 2] < 128 ? 1 : 0;
        }

        dark.ShouldBeGreaterThan(500, "the text is drawn");
        dark.ShouldBeLessThan(page.Width * page.Height / 10, "on white paper");
        Should.Throw<InvalidDataException>(() => document.Render(2, 100, 16_000_000));
        Should.Throw<InvalidDataException>(() => new PdfiumRenderer(store).Open(Path.Combine(paths.DataDirectory, "missing.pdf")));
    }

    private sealed class NoDownloads : IArchiveDownloader
    {
        public Task DownloadAsync(ToolArchive archive, string file, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Nothing is downloaded in this test.");
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
