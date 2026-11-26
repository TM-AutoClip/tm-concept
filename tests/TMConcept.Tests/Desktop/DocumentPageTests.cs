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
/// section grounds its concepts in the profile's documents ("Belgelerim") and names them; documents are deleted for good and
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
        "{\"konseptler\":[{\"baslik\":\"" + title + "\",\"ozet\":\"" + Guid.NewGuid().ToString("N") + "\"," + FakeChat.SceneList("The story of " + title)
        + ",\"kaynaklar\":[" + string.Join(",", sources) + "]}]}", new ChatUsage(1000, 0, 0, 800), null, "end_turn", "r");

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
    public async Task TypingTheSubjectWorksOutTheEstimatesOnceAfterAPauseNotOnEveryKey()
    {
        // The developer (18 Nov 2026): typing on the documents section lagged badly; every key press read the documents for two estimates.
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        using var h = new DesktopHarness(s => s.AddSingleton<TimeProvider>(clock));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        await page.Documents.Library.AddFilesAsync([Write(h, "walls.txt", "The Theodosian walls had three lines.")]);
        var section = page.Documents;
        var before = section.TypingRefreshes;

        foreach (var typed in new[] { "w", "wa", "wal", "wall", "walls" })
        {
            section.Subject = typed;
            clock.Advance(TimeSpan.FromMilliseconds(100));
        }

        section.TypingRefreshes.ShouldBe(before, "nothing is worked out while the keys come");
        clock.Advance(TMConcept.Desktop.ViewModels.DocumentSectionViewModel.TypingPause);
        await section.TypingRefresh;

        section.TypingRefreshes.ShouldBe(before + 1, "one refresh for the whole word");
        await Until(() => section.Estimate.Length > 0);
        section.Estimate.ShouldNotBeEmpty("the estimate follows the subject after the pause");
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
    public async Task DeletingADocumentIsPermanentAndNeitherCleanUpNorTheFullDeleteTakesADocument()
    {
        var discard = new RecordingDiscard();
        var confirm = new FakeConfirm { Answer = true };
        using var h = new DesktopHarness(s =>
        {
            discard.Register(s);
            s.AddSingleton<IConfirmDialog>(confirm);
        });
        await PrepareAsync(h);
        var page = await PageAsync(h);
        await page.Documents.Library.AddFilesAsync([Write(h, "walls.txt", "Walls."), Write(h, "trade.txt", "Silk.")]);
        var library = h.Get<DocumentLibrary>();
        Directory.CreateDirectory(library.Folder);
        File.WriteAllText(Path.Combine(library.Folder, "orphan.part"), "left by an interrupted add");
        File.SetLastWriteTimeUtc(Path.Combine(library.Folder, "orphan.part"), DateTime.UtcNow.AddHours(-2));
        File.WriteAllText(Path.Combine(library.Folder, "adding.part"), "being written by an add right now");

        var report = await h.Get<IDiskCleaner>().ReportAsync(None);
        var documents = report.Items.Where(i => i.Area == DiskArea.Documents).ToList();
        documents.Count.ShouldBe(3, "two documents and the leftover copies");
        DiskSelection.Unused(report.Items).Where(i => i.Key.StartsWith("document:", StringComparison.Ordinal)).ShouldBeEmpty("clean-up never takes a document");
        DiskSelection.Unused(report.Items).ShouldContain(i => i.Key == DocumentDiskSource.LeftoverKey);
        report.Items.Single(i => i.Key == DocumentDiskSource.LeftoverKey).Paths.ShouldHaveSingleItem().ShouldEndWith("orphan.part", Case.Sensitive,
            "a fresh part may be an add writing its copy");
        DiskSelection.Full(report.Items).Where(i => i.Key.StartsWith("document:", StringComparison.Ordinal)).ShouldBeEmpty(
            "0.1.28: the user's documents go only when their row is ticked");
        report.Totals.ShouldContain(t => t.Area == DiskArea.Documents);

        var walls = page.Documents.Library.Documents.Single(d => d.Title == "walls");
        await page.Documents.Library.DeleteAsync(walls);

        confirm.Messages.ShouldHaveSingleItem().ShouldContain(h.Get<Localizer>().Format("documents.delete.message", "walls",
            TMConcept.Desktop.Localization.ByteSizeText.Of(6, h.Get<Localizer>().Culture)));
        discard.Discarded.ShouldHaveSingleItem().ShouldBe(library.FileOf(walls.Document));
        File.Exists(library.FileOf(walls.Document)).ShouldBeFalse("deleted for good, not moved anywhere");
        page.Documents.Library.Documents.ShouldHaveSingleItem().Title.ShouldBe("trade");

        var ticked = (await h.Get<IDiskCleaner>().ReportAsync(None)).Items.Where(i => i.Area == DiskArea.Documents).ToList();
        (await h.Get<IDiskCleaner>().RemoveAsync(ticked, None)).Removed.Count.ShouldBe(2);
        (await library.ListAsync(page.Documents.Library.Documents[0].Document.ProfileId, None)).ShouldBeEmpty("a ticked row takes the record and passages too");
    }

    [AvaloniaFact]
    public async Task ARefusedDocumentsRunSaysWhyInTheUsersLanguageWithoutAFailureNotification()
    {
        var fake = new FakeChat();
        var notes = new RecordingNotifier();
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IChatClientFactory>(fake);
            s.AddSingleton<TMConcept.Core.Notifications.INotifier>(notes);
        });
        await PrepareAsync(h);
        var text = h.Get<Localizer>();
        text.Use("tr");
        var page = await PageAsync(h);
        await page.Documents.Library.AddFilesAsync([Write(h, "walls.txt", "The Theodosian walls had three lines.")]);

        // The document is gone underneath the page (deleted elsewhere): the run is refused before anything is sent.
        await h.Get<IDocumentStore>().DeleteAsync(page.Documents.Library.Documents[0].Document.Id, None);
        page.Documents.Count = 1;
        var reason = text["provider.fail.documents"];
        await Until(() => page.Documents.Estimate == reason);
        page.Documents.StatusIsError.ShouldBeFalse("the price line shows the reason, not \"Bir şey ters gitti\"");

        await page.Documents.GenerateAsync();

        page.Documents.Status.ShouldBe(text.Format("concepts.not_started", reason));
        page.Documents.Status.ShouldStartWith("Konsept üretimi başlamadı: seçili belgelerden hiçbiri kullanılamıyor");
        (page.Documents.StatusIsWarning, page.Documents.StatusIsError).ShouldBe((true, false));
        notes.Sent.ShouldNotContain(n => n.Kind == TMConcept.Core.Notifications.NotificationKind.Failed);
        fake.Requests.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task FindTopicsDropsATopicThatIsAlreadyAConcept()
    {
        var fake = new FakeChat()
            .Answer(_ => new ChatResponse("{\"topics\":[{\"topic\":\"Silk caravans\",\"documents\":[1],\"keywords\":[\"silk\"]},"
                + "{\"topic\":\"The three lines of walls\",\"documents\":[1],\"keywords\":[\"walls\"]}]}", new ChatUsage(900, 0, 0, 100), null, "end_turn", "r"));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var profile = await PrepareAsync(h);
        await h.Get<IConceptStore>().AddAsync([new Concept { ProfileId = profile.Id, Title = "SILK CARAVANS", Summary = "s", Content = "c", Language = ContentLanguages.English }],
            None);
        var page = await PageAsync(h);
        var topicsRow = page.Settings.Picker.Row(PipelineSteps.Topics)!;
        topicsRow.Selected = topicsRow.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        await page.Documents.Library.AddFilesAsync([Write(h, "walls.md", "# Walls\nThe Theodosian walls had three lines. Silk came too.")]);

        page.Documents.Count = 2;
        await page.Documents.FindTopicsAsync();

        page.Documents.Topics.ShouldHaveSingleItem().Text.ShouldBe("The three lines of walls", "a topic that is already a concept is not offered again");
        page.Documents.Topics[0].Topic.Keywords.ShouldBe(["walls"]);
        page.Documents.Topics[0].Topic.DocumentIds.ShouldBe([page.Documents.Library.Documents[0].Document.Id]);
        fake.Requests[0].System.ShouldContain("\"keywords\"");
    }

    [AvaloniaFact]
    public async Task DeletingAProfileDeletesItsDocumentCopies()
    {
        var discard = new RecordingDiscard();
        using var h = new DesktopHarness(s =>
        {
            discard.Register(s);
            s.AddSingleton<IConfirmDialog>(new FakeConfirm { Answer = true });
        });
        await FirstRunTests.CreateAsync(h);
        var profiles = h.Get<ProfileViewModel>();
        profiles.StartNew();
        profiles.Name = "Byzantium";
        profiles.ConceptText = "History.";
        await profiles.SaveAsync();
        var id = h.Get<AppSession>().Profile!.Id;
        var library = h.Get<DocumentLibrary>();
        await library.AddFileAsync(id, Write(h, "walls.txt", "The walls held."), null, None);
        var folder = Path.Combine(library.Folder, id.ToString("N"));
        Directory.GetFiles(folder).ShouldHaveSingleItem();

        await profiles.DeleteAsync();

        (await h.Get<IProfileStore>().ListAsync(None)).ShouldBeEmpty();
        Directory.Exists(folder).ShouldBeFalse("the plain-text copies of a deleted channel do not stay on disk");
        discard.Discarded.ShouldContain(folder);
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

    /// <summary>Keeps every notification.</summary>
    private sealed class RecordingNotifier : TMConcept.Core.Notifications.INotifier
    {
        public List<TMConcept.Core.Notifications.AppNotification> Sent { get; } = [];

        public void Notify(TMConcept.Core.Notifications.AppNotification notification) => Sent.Add(notification);
    }
}
