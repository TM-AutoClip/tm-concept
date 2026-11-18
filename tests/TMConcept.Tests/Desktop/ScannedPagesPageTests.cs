using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Documents;
using TMConcept.Core.Keys;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Runs;
using TMConcept.Desktop.DiskSpace;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Documents;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.25 on the concept page: "Text from scanned pages" offers the local OCR (only there), a document row says which pages are read and
/// reads only the missing ones with the chosen engine, and "Find topics" reads everything first when "Read everything" is ticked. Fake
/// models only.
/// </summary>
public sealed class ScannedPagesPageTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static async Task<ConceptsViewModel> PageAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "Byzantine history." };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        await h.Get<AppSession>().SelectProfileAsync(profile, None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "mine", Secret = "fake-test-key-0123456789abcdef" }, None);
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();
        var row = page.Settings.Picker.Row(PipelineSteps.Concepts)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        return page;
    }

    private static ChatResponse Answer(string text) => new(text, new ChatUsage(1_700, 0, 0, 600), null, "end_turn", "r");

    [AvaloniaFact]
    public async Task TheLocalOcrIsOfferedOnlyToReadScannedPages()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var picker = h.Get<StepPickerViewModel>();

        await picker.LoadAsync(null, ContentLanguages.Turkish, [PipelineSteps.DocumentText, PipelineSteps.DescribeImage]);

        var ocr = picker.Row(PipelineSteps.DocumentText)!.Options.Single(o => PageTextEngines.IsLocalOcr(o.Model));
        ocr.Model!.Name.ShouldContain("PaddleOCR");
        picker.Row(PipelineSteps.DescribeImage)!.Options.ShouldNotContain(o => PageTextEngines.IsLocalOcr(o.Model), "it reads text, it cannot describe an image");
        picker.Row(PipelineSteps.DocumentText)!.Options.ShouldContain(o => o.Model != null && o.Model.Id == "claude-haiku-5-5");
    }

    [AvaloniaFact]
    public async Task ARowSaysWhichPagesAreReadAndReadsOnlyTheMissingOnes()
    {
        var fake = new FakeChat().Answer(r => Answer("Konstantinopolis surları " + r.User)).Answer(r => Answer("Konstantinopolis surları " + r.User));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var page = await PageAsync(h);
        var library = page.Documents.Library;
        var text = h.Get<Localizer>();
        await library.AddFilesAsync([LargePdfTests.WritePdf(h.Paths, "scan.pdf", 4, p => p % 2 == 1)]);

        var row = library.Documents.ShouldHaveSingleItem();
        row.PagesLine.ShouldBe(text.Format("documents.pages.state_missing", 2, 4, 2));
        (row.HasMissing, row.CanReadMissing).ShouldBe((true, false));
        row.ReadMissingEstimate.ShouldBe(text["documents.pages.read_missing.none"]);
        row.ReadMissingText.ShouldBe(text.Format("documents.pages.read_missing", 2));

        var step = page.Settings.Picker.Row(PipelineSteps.DocumentText)!;
        step.Selected = step.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        library.RefreshPending();
        row.CanReadMissing.ShouldBeTrue();
        row.ReadMissingEstimate.ShouldStartWith(text.Format("documents.pages.read_missing.estimate", 2, "Claude Haiku 5.5", string.Empty)[..12]);

        await library.ReadMissingAsync(row);

        fake.Requests.Select(r => r.User).ShouldBe(["Page 2.", "Page 4."]);
        library.Status.ShouldContain(text.Format("documents.pages.done", "scan", 4, 4));
        library.Status.ShouldContain(text.Format("documents.pages.reused", "scan", 2));
        var now = library.Documents.ShouldHaveSingleItem();
        (now.HasMissing, now.Pages.Recognized).ShouldBe((false, 2));
        now.PagesLine.ShouldBe(text.Format("documents.pages.state", 4, 4) + text.Format("documents.pages.state_recognized", 2));
        (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), None)).ShouldContain(r => r.Kind == RunKind.DocumentText && r.Accepted == 2);
    }

    [AvaloniaFact]
    public async Task FindTopicsReadsEverythingFirstWhenItIsTicked()
    {
        var fake = new FakeChat()
            .Answer(_ => Answer("The walls of Theodosius had three lines and a moat."))
            .Answer(_ => new ChatResponse("{\"topics\":[{\"topic\":\"The three lines of walls\",\"documents\":[1]}]}", new ChatUsage(900, 0, 0, 100), null,
                "end_turn", "r"));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var page = await PageAsync(h);
        var section = page.Documents;
        var topics = page.Settings.Picker.Row(PipelineSteps.Topics)!;
        topics.Selected = topics.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        await section.Library.AddFilesAsync([LargePdfTests.WritePdf(h.Paths, "volume.pdf", 12, _ => true, lines: 12)]);
        section.Pages = "2-4";
        section.Count = 1;
        section.ReadAll = true;
        await Until(() => section.FindEstimate.Length > 0);
        await section.FindTopicsAsync();

        fake.Requests.Count.ShouldBe(2, "one summary of pages 2-4, then the topics call");
        fake.Requests[0].System.ShouldContain("dense note");
        fake.Requests[0].User.ShouldContain("[p. 2]");
        fake.Requests[0].User.ShouldNotContain("[p. 5]", Case.Sensitive, "only the chosen pages are read");
        fake.Requests[1].User.ShouldContain("three lines and a moat", Case.Sensitive, "the topics are found in the notes");
        section.Topics.Select(t => t.Text).ShouldBe(["The three lines of walls"]);
        (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), None)).ShouldContain(r => r.Kind == RunKind.DocumentDigest && r.Status == RunStatus.Succeeded);
    }

    [AvaloniaFact]
    public async Task KeptPagesCountInTheStorageManagerAndPagesNoDocumentHoldsAreALeftover()
    {
        using var h = new DesktopHarness();
        var page = await PageAsync(h);
        var library = page.Documents.Library;
        var file = LargePdfTests.WritePdf(h.Paths, "volume.pdf", 6, _ => true, lines: 12);
        await library.AddFilesAsync([file]);
        var document = library.Documents.ShouldHaveSingleItem().Document;
        var profile = h.Get<AppSession>().Profile!.Id;

        // An add that was canceled left the read pages of another file.
        await h.Get<IDocumentPageStore>().SavePagesAsync([new DocumentPageText { ProfileId = profile, FileSha256 = "abc", Page = 1, Text = "Okunmuş sayfa", Source = "ocr:pp-ocr-latin" }],
            None);

        var report = await h.Get<IDiskCleaner>().ReportAsync(None);

        var row = report.Items.Single(i => i.Key == DocumentDiskItem.KeyOf(document.Id));
        row.Bytes.ShouldBeGreaterThan(new FileInfo(h.Get<DocumentLibrary>().FileOf(document)).Length,
            "its kept page texts count too");
        var orphan = report.Items.Single(i => i.Key == DocumentPagesDiskItem.KeyOf(profile, "abc"));
        DiskSelection.Unused(report.Items).ShouldContain(orphan);
        (await h.Get<IDiskCleaner>().RemoveAsync([orphan], None)).Removed.Count.ShouldBe(1);
        (await h.Get<IDocumentPageStore>().MarksAsync(profile, None)).ShouldAllBe(m => m.FileSha256 == document.FileSha256);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 150 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        condition().ShouldBeTrue();
    }
}
