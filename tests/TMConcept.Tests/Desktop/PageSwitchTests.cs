using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Diagnostics;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Composition;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Storage.Database;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// Page changes without waiting (0.1.40, the developer: "program hantal"): a page's view is built once and shown again, the concepts page
/// reads its model bar and its lists once per profile and nothing when it comes back, a change made elsewhere comes in the background,
/// the look for the user's own Ollama is not waited for, and an approval shows "Approved" at once with the approved concept chosen.
/// </summary>
public sealed class PageSwitchTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    // A concept with whole scenes (approving one without them asks first).
    private const string Scenes = "[on screen: walls]\nThe walls held.\n[on screen: ships]\nThe fleet came.";

    private sealed record Counted(DesktopHarness Harness, CountingConceptStore Concepts, CountingStepModelStore Steps) : IDisposable
    {
        public void Reset()
        {
            Concepts.NewLists = 0;
            Steps.Lists = 0;
        }

        public void Dispose() => Harness.Dispose();
    }

    private static Counted Harness(Action<IServiceCollection>? more = null)
    {
        CountingConceptStore? concepts = null;
        CountingStepModelStore? steps = null;
        var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IConceptStore>(p => concepts = new CountingConceptStore(new ConceptStore(p.GetRequiredService<ConceptDatabase>())));
            s.AddSingleton<IStepModelStore>(p => steps = new CountingStepModelStore(new StepModelStore(p.GetRequiredService<ConceptDatabase>())));
            more?.Invoke(s);
        });
        h.Get<IConceptStore>();
        h.Get<IStepModelStore>();
        return new Counted(h, concepts!, steps!);
    }

    private static async Task<Profile> ProfileAsync(DesktopHarness h, string name = "Byzantium", bool select = true)
    {
        var profile = new Profile { Name = name, ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        if (select)
        {
            await h.Get<AppSession>().SelectProfileAsync(profile, None);
        }

        return profile;
    }

    private static async Task StartAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        await h.Get<AppSession>().LoadAsync(None);
    }

    private static async Task UntilAsync(Func<bool> done)
    {
        for (var i = 0; i < 500 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        done().ShouldBeTrue("waited 5 s");
    }

    [AvaloniaFact]
    public async Task ComingBackToConceptsReadsNeitherTheListsNorTheModelBar()
    {
        using var c = Harness();
        var h = c.Harness;
        await StartAsync(h);
        await ProfileAsync(h);
        var shell = h.Get<ShellViewModel>();
        await shell.StartAsync();
        var page = shell.Current.ShouldBeOfType<ConceptsViewModel>("the first tab opens");
        c.Concepts.NewLists.ShouldBe(1, "the list is read once at the start (not again when the first page is shown)");

        await shell.NavigateAsync<TrashViewModel>();
        c.Reset();
        await shell.NavigateAsync<ConceptsViewModel>();
        await shell.NavigateAsync<ProducedViewModel>();
        await shell.NavigateAsync<ConceptsViewModel>();

        c.Concepts.NewLists.ShouldBe(0, "nothing changed: the kept lists are shown");
        c.Steps.Lists.ShouldBe(0, "the model bar is kept");
        shell.Current.ShouldBeSameAs(page);
    }

    [AvaloniaFact]
    public async Task AnotherProfileIsReadOnceAndLeavingSettingsReadsTheModelBarAgain()
    {
        using var c = Harness();
        var h = c.Harness;
        await StartAsync(h);
        await ProfileAsync(h);
        var second = await ProfileAsync(h, "Ottomans", select: false);
        var shell = h.Get<ShellViewModel>();
        await shell.StartAsync();
        var page = (ConceptsViewModel)shell.Current!;
        await h.Get<IConceptStore>().AddAsync([new Concept { ProfileId = second.Id, Title = "Tulips", Summary = "s", Content = "c", Language = "en" }], None);
        c.Reset();

        // What the main window does when the profile picker changes: every page hears it, then the shown page is shown again.
        await h.Get<AppSession>().SelectProfileAsync(second, None);
        await page.ProfileChangedAsync();
        await page.ActivateAsync();

        c.Steps.Lists.ShouldBe(1, "the model bar of the other profile, once");
        c.Concepts.NewLists.ShouldBe(1, "the lists of the other profile, once");
        page.NewConcepts.ShouldHaveSingleItem().Concept.Title.ShouldBe("Tulips");

        // Settings may change keys, servers and models: back from there the model bar is read again, the lists are not.
        await shell.NavigateAsync<SettingsViewModel>();
        c.Reset();
        await shell.NavigateAsync<ConceptsViewModel>();
        c.Steps.Lists.ShouldBe(1);
        c.Concepts.NewLists.ShouldBe(0);

        // A page that does not change settings leaves the model bar alone.
        await shell.NavigateAsync<VideoViewModel>();
        c.Reset();
        await shell.NavigateAsync<ConceptsViewModel>();
        c.Steps.Lists.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task AConceptChangedElsewhereComesInTheBackgroundAndKeepsTheRows()
    {
        using var c = Harness();
        var h = c.Harness;
        await StartAsync(h);
        var profile = await ProfileAsync(h);
        var store = h.Get<IConceptStore>();
        await store.AddAsync([new Concept { ProfileId = profile.Id, Title = "Walls", Summary = "s", Content = "c", Language = "en" }], None);
        var shell = h.Get<ShellViewModel>();
        await shell.StartAsync();
        var page = (ConceptsViewModel)shell.Current!;
        var walls = page.NewConcepts.ShouldHaveSingleItem();

        // Another page adds a concept and says so while the concepts page is not shown: nothing is read then.
        await shell.NavigateAsync<VideoViewModel>();
        await store.AddAsync([new Concept { ProfileId = profile.Id, Title = "Fleet", Summary = "s", Content = "c", Language = "en" }], None);
        c.Reset();
        h.Get<ConceptEvents>().Raise(h.Get<VideoViewModel>());
        c.Concepts.NewLists.ShouldBe(0, "read when the page is shown again");

        // Back: the kept list is there at once, the new concept comes in place and the old row keeps its object.
        await shell.NavigateAsync<ConceptsViewModel>();
        await UntilAsync(() => page.NewConcepts.Count == 2);
        page.NewConcepts.ShouldContain(walls);
        page.NewConcepts[0].Concept.Title.ShouldBe("Fleet", "the newest first");
        c.Concepts.NewLists.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task APageViewIsBuiltOnceAndAnItemViewEachTime()
    {
        using var c = Harness();
        var h = c.Harness;
        await StartAsync(h);
        var locator = h.Get<ViewLocator>();
        var page = h.Get<ConceptsViewModel>();

        var view = locator.Build(page);
        view.ShouldBeOfType<TMConcept.Desktop.Views.ConceptsView>();
        locator.Build(page).ShouldBeSameAs(view, "the page's view is kept");
        locator.Build(h.Get<TrashViewModel>()).ShouldNotBeSameAs(view);

        // Still in a panel of another window: taken out of it first, never two parents.
        var panel = new StackPanel();
        panel.Children.Add(view!);
        locator.Build(page).ShouldBeSameAs(view);
        panel.Children.ShouldBeEmpty();

        // Under a parent that cannot give it back (a decorator): a new view for the new place.
        var border = new Border { Child = view };
        var other = locator.Build(page);
        other.ShouldNotBeSameAs(view);
        border.Child.ShouldBeSameAs(view);
        locator.Build(page).ShouldBeSameAs(other, "the new one is kept from now on");

        // Items (many short-lived view models) are not kept.
        var items = new ViewLocator(h.Get<IStepTimer>()).Register<ConceptCardViewModel, TextBlock>();
        var card = new ConceptCardViewModel(new Concept { Title = "x" }, h.Get<TMConcept.Desktop.Localization.Localizer>());
        items.Build(card).ShouldNotBeSameAs(items.Build(card));
    }

    [AvaloniaFact]
    public async Task AnApprovalShowsApprovedWithTheConceptChosen()
    {
        // Short test concepts: "approve anyway" is answered yes.
        using var c = Harness(s => s.AddSingleton<TMConcept.Desktop.Dialogs.IConfirmDialog>(new FakeConfirm()));
        var h = c.Harness;
        await StartAsync(h);
        var profile = await ProfileAsync(h);
        await h.Get<IConceptStore>().AddAsync(
        [
            new Concept { ProfileId = profile.Id, Title = "Walls", Summary = "s", Content = Scenes, Language = "en" },
            new Concept { ProfileId = profile.Id, Title = "Fleet", Summary = "s", Content = Scenes, Language = "en" },
            new Concept { ProfileId = profile.Id, Title = "Silk", Summary = "s", Content = Scenes, Language = "en" },
        ], None);
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();

        // A row's Approve (also the key A).
        var fleet = page.NewConcepts.Single(n => n.Concept.Title == "Fleet");
        await page.ApproveRowAsync(fleet);
        page.ShowApproved.ShouldBeTrue("the page goes to Approved at once");
        page.SelectedApproved.ShouldBeSameAs(fleet);
        page.Detail.Card.ShouldBeSameAs(fleet, "and the approved concept is open");

        // The search hid it: emptied, so the approved concept is in sight.
        page.ShowNewList();
        page.Search = "nothing like it";
        var walls = page.NewConcepts.Single(n => n.Concept.Title == "Walls");
        await page.ChooseAsync(walls);

        // The panel's Approve on the open concept.
        await page.Detail.ApproveAsync();
        await UntilAsync(() => page.ShowApproved);
        page.Search.ShouldBeEmpty();
        page.Approved.ShouldContain(walls);
        page.SelectedApproved.ShouldBeSameAs(walls);
        page.Detail.Card.ShouldBeSameAs(walls);
        (await h.Get<IConceptStore>().CountAsync(profile.Id, ConceptStatus.Approved, None)).ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task ThePickersDoNotWaitForTheLookForTheUsersOwnOllama()
    {
        var probe = new SlowProbe();
        using var c = Harness(s => s.AddSingleton<IEndpointProbe>(probe));
        var h = c.Harness;
        await StartAsync(h);
        var profile = await ProfileAsync(h);
        var picker = h.Get<StepPickerViewModel>();

        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.ConceptPage);

        // Loaded while the look still waits: the known models are there, the server is not yet.
        picker.LocalServersLooked.IsCompleted.ShouldBeFalse();
        picker.Row(PipelineSteps.Concepts)!.Options.ShouldNotContain(o => o.Model != null && EndpointCodes.IsEndpoint(o.Model.Provider));
        var changed = 0;
        picker.Changed += (_, _) => changed++;

        // The look ends: the server's models are added to the rows and the page reads its estimate again.
        probe.Answer.SetResult();
        await picker.LocalServersLooked;
        picker.Row(PipelineSteps.Concepts)!.Options.Where(o => o.Model != null && EndpointCodes.IsEndpoint(o.Model.Provider))
            .Select(o => o.Model!.Id).ShouldBe(["gemma3:4b", "llama3.2:3b"], ignoreOrder: true);
        changed.ShouldBe(1);
    }

    /// <summary>The user's Ollama on this computer, answering only when the test says so.</summary>
    private sealed class SlowProbe : IEndpointProbe
    {
        private readonly LocalModelListTests.FakeProbe _inner = new();

        public TaskCompletionSource Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<DetectedServer>> DetectAsync(CancellationToken cancellationToken)
        {
            await Answer.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await _inner.DetectAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task<EndpointListing> ListModelsAsync(string baseUrl, string token, CancellationToken cancellationToken) =>
            _inner.ListModelsAsync(baseUrl, token, cancellationToken);

        public Task<IReadOnlyList<LoadedServerModel>> LoadedAsync(LocalEndpoint endpoint, CancellationToken cancellationToken) =>
            _inner.LoadedAsync(endpoint, cancellationToken);
    }
}
