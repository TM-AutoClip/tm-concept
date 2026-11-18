using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Costs;
using TMConcept.Core.Documents;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Runs;
using TMConcept.Documents.Extractors;
using TMConcept.Documents.Ocr;
using TMConcept.Storage.Stores;
using TMConcept.Tests.Documents;

namespace TMConcept.Tests.Storage;

/// <summary>
/// 0.1.25, the read pages of a PDF are kept with the file (encrypted database): another page range of the same file and an add after a
/// cancel reuse them without asking the engine again, only the missing pages are read later, the kept pages go with the last document of
/// their file, and any mix works (the local OCR for some pages, an online vision model for others, a third model to read everything).
/// Fake engines and models only; nothing goes to a network.
/// </summary>
public sealed class PageTextStorageTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static DocumentLibrary Library(StorageHarness h) =>
        new(h.Paths, new DocumentStore(h.Database), [new PdfExtractor()], [], new LargeDocumentTests.OffEmbedder(), TimeProvider.System,
            NullLogger<DocumentLibrary>.Instance, new DocumentStore(h.Database));

    private static async Task<Profile> ProfileAsync(StorageHarness h)
    {
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.Turkish };
        await new ProfileStore(h.Database).SaveAsync(profile, None);
        return profile;
    }

    /// <summary>A PDF of six pages; the even ones are scans.</summary>
    private static string Scan(StorageHarness h) => LargePdfTests.WritePdf(h.Paths, "scan.pdf", 6, page => page % 2 == 1);

    [Fact]
    public async Task AnotherRangeOfTheSameFileDoesNotAskTheEngineForAPageReadBefore()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profile = await ProfileAsync(h);
        var file = Scan(h);
        var first = new LargePdfTests.FakeRecognizer(p => "Sur sayfası " + p.Page);

        var part = await Library(h).AddFileAsync(profile.Id, file, new DocumentReadOptions(new DocumentPageRange(1, 3), first), null, None);
        var second = new LargePdfTests.FakeRecognizer(p => "Yeni okuma " + p.Page);
        var whole = await Library(h).AddFileAsync(profile.Id, file, new DocumentReadOptions(Recognizer: second), null, None);

        first.Pages.ShouldBe([2]);
        second.Pages.ShouldBe([4, 6], "page 2 was read for the first range and is kept");
        (part.PagesReused, whole.PagesReused, whole.PagesRecognized).ShouldBe((0, 3, 2));
        whole.Document.FileSha256.ShouldBe(part.Document.FileSha256);
        (part.Document.FirstPage, whole.Document.FirstPage).ShouldBe((1, 1));
        var chunks = await new DocumentStore(h.Database).ChunksAsync(profile.Id, [whole.Document.Id], None);
        string.Join("\n", chunks.Select(c => c.Text)).ShouldContain("Sur sayfası 2");
        var kept = await new DocumentStore(h.Database).PagesAsync(profile.Id, whole.Document.FileSha256, 1, 6, None);
        kept.Select(p => (p.Page, p.Source)).ShouldBe([(1, PageTextSources.Layer), (2, PageTextSources.Unknown), (3, PageTextSources.Layer),
            (4, PageTextSources.Unknown), (5, PageTextSources.Layer), (6, PageTextSources.Unknown)]);
    }

    [Fact]
    public async Task OnlyTheMissingPagesAreReadLaterAndTheDocumentIsCutAgain()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profile = await ProfileAsync(h);
        var library = Library(h);
        var added = await library.AddFileAsync(profile.Id, Scan(h), null, None);
        var state = (await library.PageStatesAsync(profile.Id, [added.Document], None))[added.Document.Id];
        (state.Pages, state.Ready, state.Recognized).ShouldBe((6, 3, 0));
        state.Missing.ShouldBe([2, 4, 6]);

        var engine = new LargePdfTests.FakeRecognizer(p => "Hipodrom yarışları " + p.Page);
        var read = await library.ReadMissingAsync(added.Document, new DocumentReadOptions(Recognizer: engine), null, None);

        engine.Pages.ShouldBe([2, 4, 6]);
        (read.PagesRecognized, read.PagesReused).ShouldBe((3, 3));
        var now = (await library.PageStatesAsync(profile.Id, [read.Document], None))[read.Document.Id];
        (now.Ready, now.Recognized, now.Missing.Count).ShouldBe((6, 3, 0));
        var stored = (await new DocumentStore(h.Database).ListAsync(profile.Id, None)).ShouldHaveSingleItem();
        (stored.PagesWithoutText, stored.Chunks).ShouldBe((0, read.Document.Chunks));
        var chunks = await new DocumentStore(h.Database).ChunksAsync(profile.Id, [stored.Id], None);
        string.Join("\n", chunks.Select(c => c.Text)).ShouldContain("Hipodrom yarışları 4");

        // Nothing is missing any more: a second read asks no engine.
        var never = new LargePdfTests.FakeRecognizer(_ => throw new InvalidOperationException("no page is missing"));
        await library.ReadMissingAsync(stored, new DocumentReadOptions(Recognizer: never), null, None);
        never.Pages.ShouldBeEmpty();
    }

    [Fact]
    public async Task ACanceledAddKeepsWhatWasReadAndTheNextAddDoesNotReadItAgain()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profile = await ProfileAsync(h);
        var file = Scan(h);
        using var cancel = new CancellationTokenSource();
        var first = new LargePdfTests.FakeRecognizer(p =>
        {
            if (p.Page == 4)
            {
                cancel.Cancel();
            }

            return "Okunan sayfa " + p.Page;
        });

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Library(h).AddFileAsync(profile.Id, file, new DocumentReadOptions(Recognizer: first), null, cancel.Token));
        (await new DocumentStore(h.Database).ListAsync(profile.Id, None)).ShouldBeEmpty();
        var marks = await new DocumentStore(h.Database).MarksAsync(profile.Id, None);
        marks.Select(m => m.Page).Order().ToList().ShouldBe([1, 2, 3, 4], "pages 1 to 4 were read before the cancel");

        var second = new LargePdfTests.FakeRecognizer(p => "İkinci okuma " + p.Page);
        var added = await Library(h).AddFileAsync(profile.Id, file, new DocumentReadOptions(Recognizer: second), null, None);

        second.Pages.ShouldBe([6]);
        added.PagesReused.ShouldBe(4);
    }

    [Fact]
    public async Task TheKeptPagesGoWithTheLastDocumentOfTheirFile()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profile = await ProfileAsync(h);
        var file = Scan(h);
        var library = Library(h);
        var store = new DocumentStore(h.Database);
        var part = await library.AddFileAsync(profile.Id, file, new DocumentReadOptions(new DocumentPageRange(1, 2)), null, None);
        var whole = await library.AddFileAsync(profile.Id, file, null, None);

        await store.DeleteAsync(part.Document.Id, None);
        (await store.MarksAsync(profile.Id, None)).Count.ShouldBe(3, "the whole file still holds them");
        await store.DeleteAsync(whole.Document.Id, None);
        (await store.MarksAsync(profile.Id, None)).ShouldBeEmpty();

        var forgotten = await library.AddFileAsync(profile.Id, file, null, None);
        await store.ForgetAsync(profile.Id, forgotten.Document.FileSha256, None);
        (await store.MarksAsync(profile.Id, None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnyMixWorksTheLocalOcrForSomePagesAnOnlineModelForOthersAndAThirdModelReadsEverything()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profile = await ProfileAsync(h);
        var file = Scan(h);
        var library = Library(h);
        var runs = new RunStore(h.Database);

        // Pages 1-3 with the local OCR (fake networks), then the rest of the file with an online vision model (a fake one).
        var page = LocalOcrTests.Page(400, 200, (20, 30, 280, 14));
        using (var ocr = new PaddleOcrRecognizer(new LocalOcrTests.FixedDecoder(page), new LocalOcrTests.FakeDetector(), new LocalOcrTests.FakeRecognizer("Ayasofya"),
            OcrDictionary.Latin(), NullLogger.Instance))
        {
            await library.AddFileAsync(profile.Id, file, new DocumentReadOptions(new DocumentPageRange(1, 3), ocr), null, None);
        }

        var vision = new LargeDocumentTests.EveryCallChat(r => "Bizans sarayı, " + r.User);
        var reader = new DocumentTextReader(vision, new CostCalculator(), runs, TimeProvider.System, NullLogger<DocumentTextReader>.Instance);
        var key = new ApiKey { Provider = "anthropic", Label = "mine", Secret = "fake-test-key" };
        var online = await reader.StartAsync(profile.Id, new ResolvedModel(LargeDocumentTests.Paid, key, string.Empty), "scan.pdf", 2, None);
        var whole = await library.AddFileAsync(profile.Id, file, new DocumentReadOptions(Recognizer: online), null, None);
        await reader.FinishAsync(online, RunStatus.Succeeded, string.Empty);

        vision.Requests.Select(r => r.User).ShouldBe(["Page 4.", "Page 6."], "the OCR's page 2 is kept, never sent online");
        var kept = await new DocumentStore(h.Database).PagesAsync(profile.Id, whole.Document.FileSha256, 1, 6, None);
        kept.Single(p => p.Page == 2).ShouldSatisfyAllConditions(p => p.Source.ShouldBe("ocr:" + PageTextEngines.LocalOcrId), p => p.Text.ShouldBe("Ayasofya"));
        kept.Single(p => p.Page == 4).Source.ShouldBe("vision:anthropic/claude-haiku-5-5");

        // "Read everything" with a third model (a small local one): it reads the kept text of both engines, nothing is extracted again.
        var notes = new LargeDocumentTests.EveryCallChat(r => "Not: " + r.User.Length);
        var store = new DocumentStore(h.Database);
        var grounder = new DocumentGrounder(store, new LargeDocumentTests.OffEmbedder(), new PassageRetriever([new Bm25Ranker()]), NullLogger<DocumentGrounder>.Instance);
        var digester = new DocumentDigester(store, grounder, notes, new CostCalculator(), runs, TimeProvider.System, NullLogger<DocumentDigester>.Instance);
        await digester.RunAsync(profile, new ResolvedModel(LargeDocumentTests.Small, new ApiKey(), string.Empty), [whole.Document.Id], null, string.Empty, null, None);

        var read = string.Join("\n", notes.Requests.Select(r => r.User));
        read.ShouldContain("Ayasofya");
        read.ShouldContain("Bizans sarayı, Page 6.");
        vision.Requests.Count.ShouldBe(2);
    }
}
