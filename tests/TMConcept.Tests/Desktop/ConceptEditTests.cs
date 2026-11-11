using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Duplicates;
using TMConcept.Core.Keys;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;
using TMConcept.Core.Runs;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

/// <summary>Delete and edit in the concept detail panel (0.1.13): its actions, the AI edit and its save.</summary>
public sealed class ConceptEditTests
{
    private const string Secret = "fake-test-key-0123456789abcdef";

    private const string Content = "[on screen: Hagia Sophia from the sea]\nThe marble remembers.\n[pause 1 s] Sailors carved their ships into it.";

    private static async Task<(Profile Profile, Concept Concept)> PrepareAsync(DesktopHarness h, ConceptStatus status = ConceptStatus.New)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "Byzantine history." };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "mine", Secret = Secret }, CancellationToken.None);
        var concept = new Concept
        {
            ProfileId = profile.Id,
            Title = "Ship graffiti on the marble of Hagia Sophia",
            Summary = "Sailors carved ships into the marble. The video shows where.",
            Content = Content,
            Language = "en",
            Status = status,
        };
        await h.Get<IConceptStore>().AddAsync([concept], CancellationToken.None);
        return (profile, concept);
    }

    private static async Task<ConceptsViewModel> PageAsync(DesktopHarness h)
    {
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();
        var row = page.Settings.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Concepts)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        return page;
    }

    private static ChatResponse One(string title, string summary, string content) => new(
        "{\"konseptler\":[{\"baslik\":" + System.Text.Json.JsonSerializer.Serialize(title) + ",\"ozet\":" + System.Text.Json.JsonSerializer.Serialize(summary)
        + ",\"icerik\":" + System.Text.Json.JsonSerializer.Serialize(content) + "}]}", new ChatUsage(3000, 0, 0, 4000), null, "end_turn", "r");

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 150 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        condition().ShouldBeTrue();
    }

    private static async Task<Run> RunOfAsync(DesktopHarness h) =>
        (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Where(r => r.Kind == RunKind.ConceptEdit)
        .OrderBy(r => r.StartedAtUtc).Last();

    [AvaloniaFact]
    public async Task EveryRowShowsApproveAndDeleteAndThePanelsDeleteAsksWhy()
    {
        using var h = new DesktopHarness();
        var (profile, concept) = await PrepareAsync(h);
        var page = await PageAsync(h);
        var view = new TMConcept.Desktop.Views.ConceptsView { DataContext = page };
        var window = new Avalonia.Controls.Window { Content = view, Width = 1280, Height = 646 };
        h.Get<LanguageResources>().Attach(window.Resources.MergedDictionaries);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var text = h.Get<Localizer>();

        var card = page.NewConcepts.Single();
        var list = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.ListBox>().Single(l => l.Name == "NewList");
        var buttons = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(list).OfType<Avalonia.Controls.Button>().Where(b => b.IsEffectivelyVisible).ToList();
        buttons.Select(b => b.Content as string).ShouldBe([text["concepts.approve"], text["concepts.delete"]], "words, not emoji: they render everywhere");
        buttons.ShouldAllBe(b => Avalonia.Controls.ToolTip.GetTip(b) != null);

        // The panel's Delete asks why: topic, telling or just delete. The trash keeps it and the duplicate check still counts it.
        await page.ChooseAsync(card);
        Dispatcher.UIThread.RunJobs();
        var detail = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<TMConcept.Desktop.Views.ConceptDetailView>().Single();
        var trash = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(detail).OfType<Avalonia.Controls.Button>().Single(b => b.Name == "TrashButton");
        trash.IsEffectivelyVisible.ShouldBeTrue();
        trash.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var menu = Avalonia.Controls.Primitives.FlyoutBase.GetAttachedFlyout(trash).ShouldBeOfType<Avalonia.Controls.MenuFlyout>();
        var items = menu.Items.Cast<Avalonia.Controls.MenuItem>().ToList();
        items.Select(i => i.Header as string).ShouldBe([text["concepts.trash.topic"], text["concepts.trash.telling"], text["concepts.trash.just"]]);
        menu.Hide();
        items[1].Command!.Execute(null);
        await Until(() => page.NewConcepts.Count == 0);
        var trashed = (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!;
        trashed.Status.ShouldBe(ConceptStatus.Trashed);
        trashed.TrashReason.ShouldBe(TrashReason.Telling, "the reason is kept: the next prompts avoid that kind of telling");
        (await h.Get<IConceptStore>().HistoryAsync(profile.Id, CancellationToken.None)).ShouldContain(s => s.Id == concept.Id);
        page.Detail.IsEmpty.ShouldBeTrue("a deleted concept leaves the panel");
        window.Close();
    }

    [AvaloniaFact]
    public async Task AnApprovedConceptHasTheAiEditInThePanel()
    {
        using var h = new DesktopHarness();
        await PrepareAsync(h, ConceptStatus.Approved);
        var page = await PageAsync(h);
        await page.ChooseAsync(page.Approved.Single());

        await page.Detail.AiEditAsync();

        page.Detail.IsAiEditing.ShouldBeTrue();
        page.Detail.AiPrice.ShouldContain("Claude Sonnet 5.5");
    }

    [AvaloniaFact]
    public async Task AnAiEditSendsTheWishWithTheCurrentTextToTheConceptModelAndItsSaveReplacesTheConcept()
    {
        var other = new Concept { Title = "The Varangian runes in the gallery", Summary = "Norse guards carved runes. Where they are and what they say.", Language = "en" };
        var fake = new FakeChat().Answer(_ => One(other.Title, other.Summary,
            "[on screen: road map from Galata]\nThe road down to the harbour.\n[on screen: Hagia Sophia from the sea]\nThe marble remembers.\n[pause 1 s] Sailors carved their ships into it."));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, concept) = await PrepareAsync(h);
        other.ProfileId = profile.Id;
        await h.Get<IConceptStore>().AddAsync([other], CancellationToken.None);
        await h.Get<IConceptStore>().SetStatusAsync([other.Id], ConceptStatus.Produced, CancellationToken.None);
        await h.Get<IEmbeddingStore>().SaveAsync([new ConceptEmbedding { ConceptId = concept.Id, ProfileId = profile.Id, Model = "m", Dimensions = 2, Vector = SemanticCheck.ToBytes([1f, 0f]) }],
            CancellationToken.None);
        var text = h.Get<Localizer>();
        var page = await PageAsync(h);
        var card = page.NewConcepts.Single();
        await page.ChooseAsync(card);
        var detail = page.Detail;

        await detail.AiEditAsync();
        detail.IsAiEditing.ShouldBeTrue();
        detail.AiPrice.ShouldContain("Claude Sonnet 5.5");
        await detail.SendAiEditAsync();
        detail.AiStatus.ShouldBe(text["concepts.ai.empty"], "nothing is sent without a wish");
        fake.Requests.ShouldBeEmpty();

        detail.AiInstruction = "Also tell the journey on the road there.";
        await detail.SendAiEditAsync();

        var request = fake.Requests.Single();
        fake.Models.Single().Id.ShouldBe("claude-sonnet-5-5", "the concept step's model");
        fake.KeysUsed.Single().ShouldBe(Secret);
        request.User.ShouldContain("CHANGE: «Also tell the journey on the road there.»");
        request.User.ShouldContain("TITLE: «Ship graffiti on the marble of Hagia Sophia»");
        request.User.ShouldContain("[pause 1 s] Sailors carved their ships into it.", Case.Sensitive, "the user's placeholders go along");
        request.System.ShouldContain("CHANNEL CONTENT: «Byzantine history.»");
        (request.System + request.User).ShouldNotContain("Byzantium", Case.Sensitive, "the channel name is only the user's label");
        (await h.Get<IConceptWishStore>().ListAsync(profile.Id, 10, CancellationToken.None)).ShouldBeEmpty("the wish is kept only when the result is saved");
        request.System.ShouldContain("exactly as written and in the same place");
        request.Stream.ShouldBeTrue();
        request.Progress.ShouldNotBeNull();
        request.Effort.ShouldBe(page.Settings.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Concepts)!.Effort?.Value ?? string.Empty);
        request.MaxOutputTokens.ShouldBe(ConceptOutputBudget.MaxOutputTokens(fake.Models.Single()));

        // The result opens in the editor first; nothing is saved yet.
        var run = await RunOfAsync(h);
        run.Status.ShouldBe(RunStatus.Succeeded);
        run.ActualUsd.ShouldBeGreaterThan(0);
        detail.IsAiEditing.ShouldBeFalse();
        detail.IsEditing.ShouldBeTrue();
        detail.EditHeadline.ShouldBe(other.Title);
        detail.EditText.ShouldStartWith("[on screen: road map from Galata]");
        detail.EditInfo.ShouldBe(text.Format("concepts.ai.ready", text.Usd(run.ActualUsd)), "the ready line is plain, not a warning");
        detail.EditNotice.ShouldContain("#" + 2, Case.Sensitive, "the new title looks like concept #2 (made into a video): a note, not a block");
        detail.EditNotice.ShouldNotContain(text.Usd(run.ActualUsd));
        detail.Unsaved.ShouldBeTrue("an AI result always counts as unsaved");
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Title.ShouldBe(concept.Title);

        await detail.SaveEditAsync();

        var saved = (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!;
        saved.Title.ShouldBe(other.Title);
        saved.Summary.ShouldBe(other.Summary);
        saved.Content.ShouldStartWith("[on screen: road map from Galata]");
        saved.Status.ShouldBe(ConceptStatus.New);
        saved.Number.ShouldBe(1);
        (await h.Get<IEmbeddingStore>().ListAsync(profile.Id, "m", CancellationToken.None)).ShouldBeEmpty("the vectors of the old title and summary are dropped");
        card.Title.ShouldBe("#1 " + other.Title);
        (await h.Get<IConceptWishStore>().ListAsync(profile.Id, 10, CancellationToken.None)).Single().Text
            .ShouldBe("Also tell the journey on the road there.", "a saved edit's wish is kept for the next concepts");
        detail.IsViewing.ShouldBeTrue();
        detail.Notice.ShouldContain("#2", Case.Sensitive);
    }

    [AvaloniaFact]
    public async Task AnAiEditThrownAwayInTheEditorLeavesNoWish()
    {
        var fake = new FakeChat().Answer(_ => One("A funnier Hagia Sophia", "Jokes on the marble. Slang included.", "[on screen: dome]\nYo, the dome."));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, concept) = await PrepareAsync(h);
        var page = await PageAsync(h);
        await page.ChooseAsync(page.NewConcepts.Single());
        var detail = page.Detail;
        await detail.AiEditAsync();
        detail.AiInstruction = "Make it funnier, use slang.";

        await detail.SendAiEditAsync();
        detail.IsEditing.ShouldBeTrue("the result is in the editor");
        detail.CancelEdit();

        (await h.Get<IConceptWishStore>().ListAsync(profile.Id, 10, CancellationToken.None)).ShouldBeEmpty("a result the user threw away is no wish");
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Title.ShouldBe(concept.Title);

        // A plain edit of the same concept saved later does not bring the thrown-away wish back.
        await detail.EditAsync();
        await detail.SaveEditAsync();
        (await h.Get<IConceptWishStore>().ListAsync(profile.Id, 10, CancellationToken.None)).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task ACutOffOrUnreadableAiEditChangesNothingAndKeepsWhatItCost()
    {
        var fake = new FakeChat()
            .Answer(_ => throw new ProviderException("cut") { Code = "truncated", ChargedUsage = new ChatUsage(3000, 0, 0, 64_000), PartialText = "{\"konseptler\":[{\"baslik\":\"Half" })
            .Answer(_ => new ChatResponse("Sorry, no JSON today.", new ChatUsage(3000, 0, 0, 200), null, "end_turn", "r"));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (_, concept) = await PrepareAsync(h);
        var text = h.Get<Localizer>();
        var page = await PageAsync(h);
        await page.ChooseAsync(page.NewConcepts.Single());
        var detail = page.Detail;
        await detail.AiEditAsync();
        detail.AiInstruction = "Shorter.";

        await detail.SendAiEditAsync();

        var cut = await RunOfAsync(h);
        cut.Status.ShouldBe(RunStatus.Failed);
        cut.Error.ShouldBe("truncated");
        cut.ActualUsd.ShouldBeGreaterThan(0, "a cut-off answer was billed");
        cut.CostExact.ShouldBeTrue();
        detail.AiFailed.ShouldBeTrue();
        detail.AiStatus.ShouldBe(text.Format("concepts.ai.failed", ProviderErrors.Text(text, "truncated"), text.Usd(cut.ActualUsd)));
        detail.IsAiEditing.ShouldBeTrue("the box stays open");

        await detail.SendAiEditAsync();

        var unreadable = await RunOfAsync(h);
        unreadable.Error.ShouldBe("parse");
        detail.AiStatus.ShouldBe(text.Format("concepts.ai.failed", text["concepts.parse_failed"], text.Usd(unreadable.ActualUsd)));
        var stored = (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!;
        stored.Title.ShouldBe(concept.Title);
        stored.Content.ShouldBe(Content);
    }

    [AvaloniaFact]
    public async Task ARunningAiEditIsCanceledWithItsPromptCountedAndTheConceptStaysAsItWas()
    {
        var fake = new FakeChat().AnswerAsync(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("not reached");
        });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (_, concept) = await PrepareAsync(h, ConceptStatus.Approved);
        var text = h.Get<Localizer>();
        var page = await PageAsync(h);
        await page.ChooseAsync(page.Approved.Single());
        var detail = page.Detail;
        await detail.AiEditAsync();
        detail.AiInstruction = "Add the road there.";

        var send = detail.SendAiEditAsync();
        await Until(() => fake.Requests.Count == 1);
        detail.AiRunning.ShouldBeTrue();
        page.Working.ShouldBeTrue();
        detail.CancelAiEdit();
        await send;

        var run = await RunOfAsync(h);
        run.Status.ShouldBe(RunStatus.Canceled);
        run.CostExact.ShouldBeFalse();
        run.ActualUsd.ShouldBeGreaterThan(0, "the prompt may be billed");
        detail.AiStatus.ShouldBe(text.Format("concepts.ai.canceled", text.Usd(run.ActualUsd)));
        detail.AiFailed.ShouldBeFalse();
        detail.AiRunning.ShouldBeFalse();
        page.Working.ShouldBeFalse();
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Content.ShouldBe(Content);

        // Cancel again closes the box: the panel shows the text again.
        detail.CancelAiEdit();
        detail.IsAiEditing.ShouldBeFalse();
        detail.IsViewing.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task UnsavedEditorTextIsNotLostWhenTheAiBoxIsCanceled()
    {
        using var h = new DesktopHarness();
        var (_, concept) = await PrepareAsync(h);
        var page = await PageAsync(h);
        await page.ChooseAsync(page.NewConcepts.Single());
        var detail = page.Detail;

        await detail.EditAsync();
        detail.EditHeadline = "My own title";
        detail.EditText = Content + "\n[pause 2 s] My own line.";

        // AI edit from the editor, then Cancel: back to the editor with the typed text.
        await detail.AiEditAsync();
        detail.IsAiEditing.ShouldBeTrue();
        detail.Unsaved.ShouldBeTrue("the editor's text waits behind the AI box");
        detail.CancelAiEdit();
        detail.IsEditing.ShouldBeTrue();
        detail.EditHeadline.ShouldBe("My own title");
        detail.EditText.ShouldEndWith("My own line.");
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Title.ShouldBe(concept.Title);
    }

    [AvaloniaFact]
    public async Task TrashingTheCardBeingAiEditedCancelsTheEdit()
    {
        var fake = new FakeChat().AnswerAsync(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("not reached");
        });
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IChatClientFactory>(fake);
            s.AddSingleton<IConfirmDialog>(confirm);
        });
        var (_, concept) = await PrepareAsync(h);
        var page = await PageAsync(h);
        var card = page.NewConcepts.Single();
        await page.ChooseAsync(card);
        var detail = page.Detail;
        await detail.AiEditAsync();
        detail.AiInstruction = "Shorter.";
        var send = detail.SendAiEditAsync();
        await Until(() => fake.Requests.Count == 1);

        // The running AI edit counts as unsaved work: the row's Delete asks first, then goes on.
        await page.TrashRowAsync(card);
        await send;

        confirm.Messages.Single().ShouldBe(h.Get<Localizer>()["detail.unsaved"]);

        detail.AiRunning.ShouldBeFalse("the running AI edit was canceled");
        detail.IsEmpty.ShouldBeTrue();
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Trashed);
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Content.ShouldBe(Content);
    }

    [AvaloniaFact]
    public async Task AnAiEditAnswerArrivingAfterAProfileSwitchDoesNotOpen()
    {
        var gate = new TaskCompletionSource();
        var fake = new FakeChat().AnswerAsync(async (_, _) =>
        {
            await gate.Task;
            return One("Another title", "Another summary.", Content);
        });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (_, concept) = await PrepareAsync(h);
        var page = await PageAsync(h);
        await page.ChooseAsync(page.NewConcepts.Single());
        var detail = page.Detail;
        await detail.AiEditAsync();
        detail.AiInstruction = "Shorter.";
        var send = detail.SendAiEditAsync();
        await Until(() => fake.Requests.Count == 1);

        var otherProfile = new Profile { Name = "Rome", ContentLanguage = ContentLanguages.English, ConceptText = "Roman history." };
        await h.Get<IProfileStore>().SaveAsync(otherProfile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(otherProfile, CancellationToken.None);
        await page.ProfileChangedAsync();
        gate.SetResult();
        await send;

        detail.IsEditing.ShouldBeFalse("the old profile's concept does not open on the new profile's page");
        detail.IsEmpty.ShouldBeTrue();
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Title.ShouldBe(concept.Title);
    }

    [AvaloniaFact]
    public async Task AQueuedConceptIsNotEditedWithAi()
    {
        using var h = new DesktopHarness();
        var (_, concept) = await PrepareAsync(h);
        var page = await PageAsync(h);
        var card = page.NewConcepts.Single();
        await page.ChooseAsync(card);
        await h.Get<IConceptStore>().SetStatusAsync([concept.Id], ConceptStatus.Queued, CancellationToken.None);

        await page.Detail.AiEditAsync();

        page.Detail.IsAiEditing.ShouldBeFalse();
        page.Detail.Notice.ShouldBe(h.Get<Localizer>()["concepts.ai.not_editable"]);

        // Opened again, it is read-only with the note where to change it.
        await page.Detail.LoadAsync(card);
        page.Detail.CanEdit.ShouldBeFalse();
        page.Detail.ReadOnlyNote.ShouldBe(h.Get<Localizer>()["detail.queued"]);
    }
}
