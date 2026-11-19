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
using TMConcept.Desktop.Dialogs;
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

    /// <summary>Says yes to reading the missing pages online, no to every download.</summary>
    private static FakeConfirm Online()
    {
        var confirm = new FakeConfirm { Answer = false };
        confirm.Answers.Enqueue(true);
        return confirm;
    }

    private static ChatResponse Answer(string text) => new(text, new ChatUsage(1_700, 0, 0, 600), null, "end_turn", "r");

    [AvaloniaFact]
    public async Task TheLocalOcrIsOfferedOnlyToReadScannedPages()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var picker = h.Get<StepPickerViewModel>();

        await picker.LoadAsync(null, ContentLanguages.Turkish, [PipelineSteps.DocumentText, PipelineSteps.Concepts]);

        var ocr = picker.Row(PipelineSteps.DocumentText)!.Options.Single(o => PageTextEngines.IsLocalOcr(o.Model));
        ocr.Model!.Name.ShouldContain("PaddleOCR");
        picker.Row(PipelineSteps.Concepts)!.Options.ShouldNotContain(o => PageTextEngines.IsLocalOcr(o.Model), "it only reads the text of pages");
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
        (row.HasMissing, row.CanReadMissing).ShouldBe((true, true), "0.1.27: with no engine chosen the local OCR reads them");
        row.ReadMissingEstimate.ShouldContain("PaddleOCR");
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
    public async Task FindTopicsReadsTheMissingPagesFirstAndNeverAgain()
    {
        var topicsAnswer = new ChatResponse("{\"topics\":[{\"topic\":\"The land walls\",\"documents\":[1]}]}", new ChatUsage(900, 0, 0, 100), null, "end_turn", "r");
        var fake = new FakeChat()
            .Answer(r => Answer("Konstantinopolis surları " + r.User)).Answer(r => Answer("Konstantinopolis surları " + r.User))
            .Answer(_ => topicsAnswer).Answer(_ => topicsAnswer);
        var confirm = Online();
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake).AddSingleton<IConfirmDialog>(confirm));
        var page = await PageAsync(h);
        var section = page.Documents;
        var library = section.Library;
        await library.AddFilesAsync([LargePdfTests.WritePdf(h.Paths, "scan.pdf", 4, p => p % 2 == 1)]);
        library.Documents.ShouldHaveSingleItem().MissingCount.ShouldBe(2, "added without reading its scanned pages");
        var step = page.Settings.Picker.Row(PipelineSteps.DocumentText)!;
        step.Selected = step.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        var topics = page.Settings.Picker.Row(PipelineSteps.Topics)!;
        topics.Selected = topics.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        section.Count = 1;

        await section.FindTopicsAsync();

        fake.Requests.Select(r => r.User).Take(2).ShouldBe(["Page 2.", "Page 4."], "the missing pages were read before the topics call");
        fake.Requests.Count.ShouldBe(3);
        section.Topics.Select(t => t.Text).ShouldBe(["The land walls"]);
        library.Documents.ShouldHaveSingleItem().HasMissing.ShouldBeFalse();

        await section.FindTopicsAsync();

        fake.Requests.Count.ShouldBe(4, "pages read once are never read again");
        confirm.Asked.Count(t => t == h.Get<Localizer>()["documents.pages.online.title"]).ShouldBe(1, "the online reading was asked once");
    }

    [AvaloniaFact]
    public async Task WritingFromDocumentsReadsTheMissingPagesFirst()
    {
        var fake = new FakeChat()
            .Answer(r => Answer("Konstantinopolis surları " + r.User)).Answer(r => Answer("Konstantinopolis surları " + r.User))
            .Answer(_ => new ChatResponse("{\"konseptler\":[{\"baslik\":\"The land walls\",\"ozet\":\"" + Guid.NewGuid().ToString("N")
                + "\",\"icerik\":\"[on screen: walls]\\nThe walls.\",\"kaynaklar\":[1]}]}", new ChatUsage(1000, 0, 0, 800), null, "end_turn", "r"));
        var confirm = Online();
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake).AddSingleton<IConfirmDialog>(confirm));
        var page = await PageAsync(h);
        var section = page.Documents;
        await section.Library.AddFilesAsync([LargePdfTests.WritePdf(h.Paths, "scan.pdf", 4, p => p % 2 == 1)]);
        var step = page.Settings.Picker.Row(PipelineSteps.DocumentText)!;
        step.Selected = step.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        section.Count = 1;

        await section.GenerateAsync();

        fake.Requests.Count.ShouldBe(3);
        fake.Requests.Select(r => r.User).Take(2).ShouldBe(["Page 2.", "Page 4."], "the missing pages were read before the concept call");
        fake.Requests[2].User.ShouldContain("Konstantinopolis surları Page 2.", Case.Sensitive, "the pages just read are part of the passages");
        section.Library.Documents.ShouldHaveSingleItem().HasMissing.ShouldBeFalse();
        confirm.Messages[0].ShouldContain("Claude Haiku 5.5", Case.Sensitive, "the question names the engine and its estimate");
    }

    [AvaloniaFact]
    public async Task ADeclinedOnlineReadingGoesOnWithThePagesAlreadyRead()
    {
        var fake = new FakeChat()
            .Answer(_ => new ChatResponse("{\"konseptler\":[{\"baslik\":\"The land walls\",\"ozet\":\"" + Guid.NewGuid().ToString("N")
                + "\",\"icerik\":\"[on screen: walls]\\nThe walls.\",\"kaynaklar\":[1]}]}", new ChatUsage(1000, 0, 0, 800), null, "end_turn", "r"));
        var confirm = new FakeConfirm { Answer = false };
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake).AddSingleton<IConfirmDialog>(confirm));
        var page = await PageAsync(h);
        var section = page.Documents;
        await section.Library.AddFilesAsync([LargePdfTests.WritePdf(h.Paths, "scan.pdf", 4, p => p % 2 == 1)]);
        var step = page.Settings.Picker.Row(PipelineSteps.DocumentText)!;
        step.Selected = step.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        section.Count = 1;

        await section.GenerateAsync();

        confirm.Asked.ShouldBe([h.Get<Localizer>()["documents.pages.online.title"]]);
        fake.Requests.ShouldHaveSingleItem("no page was sent; the concept call used the pages already read");
        section.Library.Documents.ShouldHaveSingleItem().MissingCount.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task ATickedDocumentWhosePagesWereNotReadIsLeftOutAndNoneReadyStops()
    {
        var fake = new FakeChat();
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake).AddSingleton<IConfirmDialog>(new FakeConfirm { Answer = false }));
        var page = await PageAsync(h);
        var section = page.Documents;
        var library = section.Library;
        var notes = Path.Combine(h.Paths.DataDirectory, "incoming", "notes.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(notes)!);
        await File.WriteAllTextAsync(notes, "The land walls of Constantinople had three lines and a moat.");
        await library.AddFilesAsync([notes]);
        await library.AddFilesAsync([LargePdfTests.WritePdf(h.Paths, "scan.pdf", 2, _ => false)]);
        var scan = library.Documents.Single(d => d.HasMissing);
        scan.Ready.ShouldBeFalse();
        scan.Checked = true;
        var step = page.Settings.Picker.Row(PipelineSteps.DocumentText)!;
        step.Selected = step.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        section.Count = 1;

        await section.GenerateAsync();

        fake.Requests.ShouldBeEmpty("the only ticked document is still unread: no run on an empty context");
        section.Status.ShouldBe(h.Get<Localizer>()["documents.none_ready"]);
    }

    [AvaloniaFact]
    public async Task PagesThatCouldNotBeReadAreNotReadAgainAtEveryPress()
    {
        var topicsAnswer = new ChatResponse("{\"topics\":[{\"topic\":\"The land walls\",\"documents\":[1]}]}", new ChatUsage(900, 0, 0, 100), null, "end_turn", "r");
        var fake = new FakeChat()
            .Answer(_ => throw new InvalidOperationException("The page could not be read."))
            .Answer(_ => throw new InvalidOperationException("The page could not be read."))
            .Answer(_ => topicsAnswer).Answer(_ => topicsAnswer);
        var confirm = Online();
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake).AddSingleton<IConfirmDialog>(confirm));
        var page = await PageAsync(h);
        var section = page.Documents;
        var library = section.Library;
        await library.AddFilesAsync([LargePdfTests.WritePdf(h.Paths, "scan.pdf", 4, p => p % 2 == 1)]);
        var step = page.Settings.Picker.Row(PipelineSteps.DocumentText)!;
        step.Selected = step.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        var topics = page.Settings.Picker.Row(PipelineSteps.Topics)!;
        topics.Selected = topics.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        section.Count = 1;

        await section.FindTopicsAsync();
        library.Documents.ShouldHaveSingleItem().MissingCount.ShouldBe(2, "both pages failed");
        fake.Requests.Count.ShouldBe(3);

        await section.FindTopicsAsync();

        fake.Requests.Count.ShouldBe(4, "only the topics call: the pages that failed are not sent again");
        confirm.Asked.Count(t => t == h.Get<Localizer>()["documents.pages.online.title"]).ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task WithTheOcrInstalledAndNoEngineChosenTheOptionalPartsAreAskedOnceWithTheirOwnText()
    {
        var confirm = new FakeConfirm { Answer = false };
        using var h = new DesktopHarness(s => s.AddSingleton<IConfirmDialog>(confirm));
        var page = await PageAsync(h);
        var library = page.Documents.Library;
        var text = h.Get<Localizer>();
        var ocr = h.Get<TMConcept.Documents.Ocr.LocalOcrEngine>();
        foreach (var file in ocr.Missing)
        {
            var path = Path.Combine(ocr.Folder, file.ExecutablePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
        }

        ocr.Ready.ShouldBeTrue("installed in an earlier version (fake model files here)");
        await library.AddFilesAsync([LargePdfTests.WritePdf(h.Paths, "scan.pdf", 2, p => p == 1)]);

        await library.ReadMissingAsync(library.Documents.ShouldHaveSingleItem());

        var question = confirm.Messages.ShouldHaveSingleItem("one question, not one for the OCR's list and another for its optional parts");
        question.ShouldNotContain(text["install.purpose.document_text"], Case.Sensitive, "cancelling it does not leave the pages unread");
        question.ShouldContain("PDFium");
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
