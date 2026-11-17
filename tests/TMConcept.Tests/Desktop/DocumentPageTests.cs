using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Documents;
using TMConcept.Core.Keys;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.DiskSpace;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.21, the concept page's three sources: "Konu öner", "Fikirden" and "Belgelerden" each ADD to the one list in any order; the documents
/// section grounds its concepts in the profile's documents ("Belgelerim") and names them; documents are deleted through the recycle bin and
/// the storage manager keeps them out of "Clean up unused"; the meaning model is downloaded only after a yes. Fake models only.
/// </summary>
public sealed class DocumentPageTests
{
    private const string Secret = "fake-test-key-0123456789abcdef";

    private static readonly CancellationToken None = CancellationToken.None;

    private static async Task<Profile> PrepareAsync(DesktopHarness h, string name = "Byzantium")
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = name, ContentLanguage = ContentLanguages.English, ConceptText = "Byzantine history." };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        await h.Get<AppSession>().SelectProfileAsync(profile, None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "mine", Secret = Secret }, None);
        return profile;
    }

    private static async Task<ConceptsViewModel> PageAsync(DesktopHarness h)
    {
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();
        var row = page.Settings.Picker.Row(PipelineSteps.Concepts)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        return page;
    }

    private static string Write(DesktopHarness h, string name, string text)
    {
        var file = Path.Combine(h.Paths.DataDirectory, "incoming", name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }

    private static ChatResponse Grounded(string title, params int[] sources) => new(
        "{\"konseptler\":[{\"baslik\":\"" + title + "\",\"ozet\":\"" + Guid.NewGuid().ToString("N") + "\",\"icerik\":\"[on screen: walls]\\nThe story of " + title
        + ".\",\"kaynaklar\":[" + string.Join(",", sources) + "]}]}", new ChatUsage(1000, 0, 0, 800), null, "end_turn", "r");

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 150 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        condition().ShouldBeTrue();
    }

    private static T Named<T>(Visual root, string name)
        where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(new Point(0, 0), root) ?? throw new InvalidOperationException(control.Name), control.Bounds.Size);

    [AvaloniaFact]
    public async Task TheThreeSourcesAppendToOneListInAnyOrder()
    {
        var fake = new FakeChat().Concepts(["Greek fire"]).Answer(_ => Grounded("The Theodosian walls", 1)).Concepts(["The Nika riots"]);
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        await page.Documents.Library.AddFilesAsync([Write(h, "walls.txt", "The Theodosian walls had three lines.\n\nThey held for a thousand years.")]);
        page.Documents.Library.HasReady.ShouldBeTrue();

        page.Ideas.Count = 1;
        await page.Ideas.GenerateAsync();
        var first = page.NewConcepts.Single();
        page.Documents.Subject = "walls";
        page.Documents.Count = 1;
        page.Documents.GenerateText.ShouldBe(h.Get<Localizer>().Format("documents.generate", 1));
        await page.Documents.GenerateAsync();
        page.Ideas.Idea = "Riots";
        await page.Ideas.GenerateAsync();

        page.NewConcepts.Count.ShouldBe(3);
        page.NewConcepts.ShouldContain(first, "the earlier concepts stay");
        page.NewConcepts.Select(c => c.Concept.Title).ShouldBe(["The Nika riots", "The Theodosian walls", "Greek fire"], "the newest first, all in one list");
        fake.Requests[0].User.Contains("DOCUMENT", StringComparison.Ordinal).ShouldBeFalse("an idea run sends no documents");
        fake.Requests[2].User.Contains("DOCUMENT", StringComparison.Ordinal).ShouldBeFalse();
        var documents = fake.Requests[1];
        documents.System.ShouldContain("Use ONLY the facts in the DOCUMENT PASSAGES");
        documents.User.ShouldContain("SUBJECT: «walls»");
        documents.User.ShouldContain("DOCUMENTS (number: title):\n1: «walls»");
        documents.User.ShouldContain("[1]: «The Theodosian walls had three lines.\nThey held for a thousand years.»");
        var saved = await h.Get<IConceptStore>().ListAsync(page.Documents.Library.Documents[0].Document.ProfileId, [ConceptStatus.New], false, None);
        saved.Single(c => c.Title == "The Theodosian walls").Sources.ShouldBe("walls");
        saved.Single(c => c.Title == "Greek fire").Sources.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task FoundTopicsMakeOneGroundedConceptEachAndLeaveTheList()
    {
        var fake = new FakeChat()
            .Answer(_ => new ChatResponse("{\"topics\":[{\"topic\":\"The three lines of walls\",\"documents\":[1]},{\"topic\":\"Silk caravans\",\"documents\":[2]}]}",
                new ChatUsage(900, 0, 0, 100), null, "end_turn", "r"))
            .Answer(_ => Grounded("Walls", 1))
            .Answer(_ => Grounded("Silk", 1));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        var topicsRow = page.Settings.Picker.Row(PipelineSteps.Topics)!;
        topicsRow.Selected = topicsRow.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        await page.Documents.Library.AddFilesAsync([
            Write(h, "walls.md", "# Walls\nThe Theodosian walls had three lines."),
            Write(h, "trade.txt", "Silk came to Constantinople by caravan."),
        ]);

        page.Documents.Count = 2;
        await page.Documents.FindTopicsAsync();

        page.Documents.Topics.Select(t => t.Text).ShouldBe(["The three lines of walls", "Silk caravans"]);
        page.Documents.Topics.ShouldAllBe(t => t.Checked && t.Documents.Length > 0);
        fake.Requests[0].User.ShouldContain("DOCUMENT PASSAGES");
        fake.Requests[0].User.ShouldEndWith("Suggest 2 topics now.");
        page.Documents.GenerateText.ShouldBe(h.Get<Localizer>().Format("documents.generate.topics", 2));

        await page.Documents.GenerateAsync();

        fake.Requests.Count.ShouldBe(3, "one call per topic");
        fake.Requests[1].User.ShouldContain("TOPICS (one concept each, in order):\n1. «The three lines of walls»");
        fake.Requests[1].User.ShouldContain("three lines");
        fake.Requests[2].User.ShouldContain("1. «Silk caravans»");
        fake.Requests[2].User.ShouldContain("Silk came to Constantinople");
        page.NewConcepts.Count.ShouldBe(2);
        page.Documents.Topics.ShouldBeEmpty("topics that got their concept leave the list");
    }

    [AvaloniaFact]
    public async Task WithoutADocumentTheSectionSaysWhyAndSendsNothing()
    {
        var fake = new FakeChat();
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        page.Documents.Refresh();

        page.Documents.CanGenerate.ShouldBeFalse();
        page.Documents.Blocked.ShouldBe(h.Get<Localizer>()["documents.none_ready"]);
        page.Documents.CanFind.ShouldBeFalse();
        await page.Documents.GenerateAsync();
        fake.Requests.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task DocumentsBelongToTheProfileTheyWereAddedIn()
    {
        using var h = new DesktopHarness();
        var first = await PrepareAsync(h);
        var page = await PageAsync(h);
        await page.Documents.Library.AddFilesAsync([Write(h, "walls.txt", "Walls.")]);
        page.Documents.Library.Documents.Count.ShouldBe(1);

        var other = new Profile { Name = "Ottoman", ContentLanguage = ContentLanguages.Turkish, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(other, None);
        await h.Get<AppSession>().SelectProfileAsync(other, None);
        await page.ProfileChangedAsync();

        page.Documents.Library.Documents.ShouldBeEmpty();
        page.Documents.Library.WikipediaLanguage.ShouldBe("tr");
        await h.Get<AppSession>().SelectProfileAsync(first, None);
        await page.ProfileChangedAsync();
        page.Documents.Library.Documents.ShouldHaveSingleItem().Title.ShouldBe("walls");
    }

    [AvaloniaFact]
    public async Task ARejectedDocumentSaysWhyInTheUsersLanguage()
    {
        using var h = new DesktopHarness();
        await PrepareAsync(h);
        h.Get<Localizer>().Use("tr");
        var page = await PageAsync(h);
        var file = Write(h, "walls.txt", "Walls.");
        await page.Documents.Library.AddFilesAsync([file]);
        await page.Documents.Library.AddFilesAsync([file, Write(h, "deck.pptx", "x")]);

        page.Documents.Library.StatusIsError.ShouldBeTrue();
        page.Documents.Library.Status.ShouldContain("walls.txt: bu profilde zaten var (\"walls\")");
        page.Documents.Library.Status.ShouldContain("deck.pptx: yalnız PDF, Word (.docx), metin ve Markdown dosyaları eklenebilir");
        page.Documents.Library.Address = "https://example.org/page";
        await page.Documents.Library.AddWebAsync();
        page.Documents.Library.Status.ShouldContain("adrese ulaşılamadı", Case.Sensitive, "the test network refuses every request");
    }

    [AvaloniaFact]
    public async Task DeletingADocumentUsesTheRecycleBinAndCleanUpNeverTakesADocument()
    {
        var bin = new FakeBin();
        var confirm = new FakeConfirm { Answer = true };
        using var h = new DesktopHarness(s =>
        {
            s.RemoveAll<IRecycleBin>();
            s.AddSingleton<IRecycleBin>(bin);
            s.AddSingleton<IConfirmDialog>(confirm);
        });
        await PrepareAsync(h);
        var page = await PageAsync(h);
        await page.Documents.Library.AddFilesAsync([Write(h, "walls.txt", "Walls."), Write(h, "trade.txt", "Silk.")]);
        var library = h.Get<DocumentLibrary>();
        Directory.CreateDirectory(library.Folder);
        File.WriteAllText(Path.Combine(library.Folder, "orphan.part"), "left by an interrupted add");

        var report = await h.Get<IDiskCleaner>().ReportAsync(None);
        var documents = report.Items.Where(i => i.Area == DiskArea.Documents).ToList();
        documents.Count.ShouldBe(3, "two documents and the leftover copies");
        DiskSelection.Unused(report.Items).Where(i => i.Key.StartsWith("document:", StringComparison.Ordinal)).ShouldBeEmpty("clean-up never takes a document");
        DiskSelection.Unused(report.Items).ShouldContain(i => i.Key == DocumentDiskSource.LeftoverKey);
        DiskSelection.Full(report.Items).Count(i => i.Key.StartsWith("document:", StringComparison.Ordinal)).ShouldBe(2, "the full delete takes them");
        report.Totals.ShouldContain(t => t.Area == DiskArea.Documents);

        var walls = page.Documents.Library.Documents.Single(d => d.Title == "walls");
        await page.Documents.Library.DeleteAsync(walls);

        confirm.Asked.ShouldHaveSingleItem();
        bin.Moved.ShouldHaveSingleItem().ShouldBe(library.FileOf(walls.Document));
        page.Documents.Library.Documents.ShouldHaveSingleItem().Title.ShouldBe("trade");

        var full = DiskSelection.Full((await h.Get<IDiskCleaner>().ReportAsync(None)).Items).Where(i => i.Area == DiskArea.Documents).ToList();
        (await h.Get<IDiskCleaner>().RemoveAsync(full, None)).Removed.Count.ShouldBe(2);
        (await library.ListAsync(page.Documents.Library.Documents[0].Document.ProfileId, None)).ShouldBeEmpty("the full delete takes the records and passages too");
    }

    [AvaloniaFact]
    public async Task TheMeaningModelIsDownloadedOnlyAfterAYes()
    {
        var confirm = new FakeConfirm { Answer = false };
        using var h = new DesktopHarness(s => s.AddSingleton<IConfirmDialog>(confirm));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        page.Documents.Library.EmbedderReady.ShouldBeFalse();
        page.Documents.Library.SearchLine.ShouldBe(h.Get<Localizer>()["documents.search.keyword"]);

        await page.Documents.Library.InstallEmbedderAsync();

        confirm.Messages.ShouldHaveSingleItem().ShouldContain("Multilingual E5 small · model.onnx — huggingface.co — ~118 MB");
        page.Documents.Library.EmbedderReady.ShouldBeFalse("declined: nothing was downloaded");
        Directory.Exists(h.Get<TMConcept.Documents.Embeddings.E5Embedder>().Folder).ShouldBeFalse();
        page.Documents.Library.Status.ShouldBe(h.Get<Localizer>()["install.declined"]);
    }

    [AvaloniaFact]
    public async Task PageAt1280ShowsTheThreeSectionsSideBySideAndTheLibraryOverThePage()
    {
        using var h = new DesktopHarness();
        await PrepareAsync(h);
        h.Get<Localizer>().Use("tr");
        var page = await PageAsync(h);
        var view = new TMConcept.Desktop.Views.ConceptsView { DataContext = page };
        var window = new Window { Content = view, Width = 1280, Height = 646 };
        h.Get<LanguageResources>().Attach(window.Resources.MergedDictionaries);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var sections = new[] { "TopicSection", "IdeaSection", "DocumentSection" }.Select(n => BoundsIn(Named<Control>(view, n), view)).ToList();
        sections.ShouldAllBe(r => r.Width > 300 && r.Height > 100);
        sections.Select(r => r.Y).Distinct().ShouldHaveSingleItem("the three stand side by side");
        sections[0].Intersects(sections[1]).ShouldBeFalse();
        sections[1].Intersects(sections[2]).ShouldBeFalse();
        Named<Button>(Named<Control>(view, "DocumentFooter"), "GenerateButton").IsEffectivelyVisible.ShouldBeTrue();
        Named<Control>(view, "DocumentsPanel").IsVisible.ShouldBeFalse();

        page.Documents.Library.Toggle();
        Dispatcher.UIThread.RunJobs();
        Named<Control>(view, "DocumentsPanel").IsVisible.ShouldBeTrue();
        Named<Button>(view, "AddFilesButton").IsEffectivelyVisible.ShouldBeTrue();
        window.Close();
    }

    /// <summary>A recycle bin that deletes what it is given.</summary>
    private sealed class FakeBin : IRecycleBin
    {
        public List<string> Moved { get; } = [];

        public Task MoveAsync(string folder, CancellationToken cancellationToken)
        {
            Moved.Add(folder);
            if (File.Exists(folder))
            {
                File.Delete(folder);
            }
            else if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            return Task.CompletedTask;
        }
    }
}
