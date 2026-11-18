using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Keys;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Runs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Documents;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.24 on the concept page: a picked PDF waits with its pages and its estimate, a page range is checked before it is added, its scanned
/// pages are read by the chosen text extraction model (a fake one) and counted; a run takes a page range, and "read everything" summarises
/// first and writes the concepts from the notes, with the plan shown before. Fake models only.
/// </summary>
public sealed class LargeDocumentPageTests
{
    private const string Secret = "fake-test-key-0123456789abcdef";

    private static readonly CancellationToken None = CancellationToken.None;

    private static async Task<ConceptsViewModel> PageAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "Byzantine history." };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        await h.Get<AppSession>().SelectProfileAsync(profile, None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "mine", Secret = Secret }, None);
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();
        var row = page.Settings.Picker.Row(PipelineSteps.Concepts)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        return page;
    }

    private static ChatResponse Answer(string text) => new(text, new ChatUsage(1_700, 0, 0, 600), null, "end_turn", "r");

    [AvaloniaFact]
    public async Task APickedPdfWaitsWithItsPagesAndARangeIsCheckedBeforeItIsAdded()
    {
        using var h = new DesktopHarness();
        var page = await PageAsync(h);
        var library = page.Documents.Library;
        var file = LargePdfTests.WritePdf(h.Paths, "volume.pdf", 30, _ => true, lines: 12);
        var text = h.Get<Localizer>();

        await library.StageAsync([file]);

        var row = library.Pending.ShouldHaveSingleItem();
        library.HasPending.ShouldBeTrue();
        row.Info.ShouldStartWith(text.Format("documents.import.info", 30, string.Empty).TrimEnd(' ', '·'));
        row.HasImagePages.ShouldBeFalse();
        row.Estimate.ShouldStartWith(text.Format("documents.import.reading", 30, string.Empty)[..10]);
        row.Range = "25-40";
        (row.RangeValid, row.EstimateIsError).ShouldBe((false, true));
        row.Estimate.ShouldBe(text.Format("documents.import.pages.invalid", 30));
        await library.AddPendingAsync();
        library.Status.ShouldBe(text.Format("documents.import.fix_pages", "volume.pdf"));
        library.Documents.ShouldBeEmpty();

        row.Range = "5-9";
        (row.RangeValid, row.PagesChosen).ShouldBe((true, 5));
        await library.AddPendingAsync();

        library.HasPending.ShouldBeFalse();
        var added = library.Documents.ShouldHaveSingleItem();
        added.Title.ShouldBe("volume (p. 5–9)");
        added.Document.Pages.ShouldBe(5);
    }

    [AvaloniaFact]
    public async Task TheScannedPagesOfAPdfAreReadByTheTextExtractionModelWithTheirCostCounted()
    {
        var fake = new FakeChat().Answer(r => Answer("Konstantinopolis surları " + r.User)).Answer(r => Answer("Konstantinopolis surları " + r.User));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var page = await PageAsync(h);
        var library = page.Documents.Library;
        var file = LargePdfTests.WritePdf(h.Paths, "scan.pdf", 4, p => p % 2 == 1);
        var text = h.Get<Localizer>();

        await library.StageAsync([file]);
        var waiting = library.Pending.ShouldHaveSingleItem();
        waiting.HasImagePages.ShouldBeTrue();
        (waiting.CanExtract, waiting.Extract).ShouldBe((false, false), "no text extraction model is chosen yet");
        waiting.Estimate.ShouldContain(text["documents.import.extract.none"]);
        var step = page.Settings.Picker.Row(PipelineSteps.DocumentText)!;
        step.Selected = step.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        var haiku = step.Selected.Model!;
        library.RefreshPending();
        waiting.CanExtract.ShouldBeTrue();
        waiting.Extract = true;
        waiting.Estimate.ShouldContain(text.Format("documents.import.extract.estimate", 2, "Claude Haiku 5.5", string.Empty)[..20]);

        await library.AddPendingAsync();

        var added = library.Documents.ShouldHaveSingleItem();
        (added.Ready, added.Document.PagesWithoutText).ShouldBe((true, 0));
        fake.Requests.Count.ShouldBe(2);
        fake.Requests.ShouldAllBe(r => r.Images != null && r.Images.Count == 1);
        library.Status.ShouldContain(text.Format("documents.added_pages_read", "scan", 2, "Claude Haiku 5.5", text.Usd(2 * ((1_700 * haiku.InputUsd) + (600 * haiku.OutputUsd)) / 1_000_000m)));
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), None)).Single(r => r.Kind == RunKind.DocumentText);
        (run.Status, run.Accepted, run.InputTokens).ShouldBe((RunStatus.Succeeded, 2, 3_400L));
    }

    [AvaloniaFact]
    public async Task ReadingEverythingSummarisesFirstAndTheConceptIsWrittenFromTheNotes()
    {
        var fake = new FakeChat()
            .Answer(_ => Answer("The walls of Theodosius had three lines and a moat; they held for a thousand years."))
            .Answer(_ => new ChatResponse("{\"konseptler\":[{\"baslik\":\"Three lines of walls\",\"ozet\":\"" + Guid.NewGuid().ToString("N")
                + "\",\"icerik\":\"[on screen: walls]\\nThe walls.\",\"kaynaklar\":[1]}]}", new ChatUsage(1000, 0, 0, 800), null, "end_turn", "r"));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var page = await PageAsync(h);
        var section = page.Documents;
        var file = LargePdfTests.WritePdf(h.Paths, "volume.pdf", 12, _ => true, lines: 12);
        await section.Library.AddFilesAsync([file]);
        var text = h.Get<Localizer>();

        section.Pages = "abc";
        section.Blocked.ShouldBe(text["documents.plan.pages_invalid"]);
        section.Pages = "2-4";
        section.Count = 1;
        section.ReadAll = true;
        await Until(() => section.Plan.StartsWith(text.Format("documents.plan.read_all", "x", 0, 0, 0, "x", "x")[..12], StringComparison.Ordinal));
        section.Plan.ShouldContain("Claude Sonnet 5.5");
        section.PlanIsError.ShouldBeFalse();

        await section.GenerateAsync();

        fake.Requests.Count.ShouldBe(2);
        fake.Requests[0].System.ShouldContain("dense note");
        fake.Requests[0].User.ShouldContain("[p. 2]");
        fake.Requests[0].User.ShouldNotContain("[p. 5]", Case.Sensitive, "only the chosen pages are read");
        fake.Requests[1].User.ShouldContain("three lines and a moat", Case.Sensitive, "the concept is written from the notes");
        page.NewConcepts.ShouldHaveSingleItem().Concept.Title.ShouldBe("Three lines of walls");
        (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), None)).ShouldContain(r => r.Kind == RunKind.DocumentDigest && r.Status == RunStatus.Succeeded);
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
