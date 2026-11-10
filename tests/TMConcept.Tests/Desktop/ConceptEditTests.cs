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
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

/// <summary>Delete and edit on the generated concepts (10 Nov 2026): the card actions, the AI edit and its save.</summary>
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
        var row = page.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Concepts)!;
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
    public async Task EveryNewCardShowsItsActionsWithoutOpeningAndDeleteMovesItToTheTrash()
    {
        using var h = new DesktopHarness();
        var (profile, concept) = await PrepareAsync(h);
        var page = await PageAsync(h);
        var view = new TMConcept.Desktop.Views.ConceptsView { DataContext = page };
        var window = new Avalonia.Controls.Window { Content = view, Width = 1280, Height = 820 };
        h.Get<LanguageResources>().Attach(window.Resources.MergedDictionaries);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var card = page.NewConcepts.Single();
        card.Expanded.ShouldBeFalse();
        var list = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.ListBox>().Single(l => l.Name == "NewList");
        var buttons = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(list).OfType<Avalonia.Controls.Button>().Where(b => b.IsEffectivelyVisible).ToList();
        buttons.Select(b => b.Content as string).ShouldBe(["✨", "✎", "✓", "🗑"], "the actions are there without opening the card");
        buttons.ShouldAllBe(b => Avalonia.Controls.ToolTip.GetTip(b) != null);

        // The "?" next to the list says what the icons do.
        var text = h.Get<Localizer>();
        text["concepts.keys"].ShouldContain("🗑");

        // 🗑 = to the trash: the trash keeps it and the duplicate check still counts it.
        buttons.Single(b => (b.Content as string) == "🗑").Command!.Execute(card);
        await Until(() => page.NewConcepts.Count == 0);
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Trashed);
        (await h.Get<IConceptStore>().HistoryAsync(profile.Id, CancellationToken.None)).ShouldContain(s => s.Id == concept.Id);
        window.Close();
    }

    [AvaloniaFact]
    public async Task AnApprovedConceptHasTheAiEditNextToItsTitle()
    {
        using var h = new DesktopHarness();
        await PrepareAsync(h, ConceptStatus.Approved);
        var page = await PageAsync(h);
        var view = new TMConcept.Desktop.Views.ConceptsView { DataContext = page };
        var window = new Avalonia.Controls.Window { Content = view, Width = 1280, Height = 820 };
        h.Get<LanguageResources>().Attach(window.Resources.MergedDictionaries);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var ai = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.Button>()
            .Single(b => (b.Content as string) == "✨" && b.IsEffectivelyVisible);
        ai.Command!.Execute(ai.CommandParameter);
        await Until(() => page.AiEditing);
        page.Listing.ShouldBeFalse("the AI edit box takes the list's place");
        page.AiEditPrice.ShouldContain("Claude Sonnet 5.5");
        window.Close();
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

        await page.AiEditAsync(card);
        page.AiEditing.ShouldBeTrue();
        page.AiEditPrice.ShouldContain("Claude Sonnet 5.5");
        await page.SendAiEditAsync();
        page.AiEditStatus.ShouldBe(text["concepts.ai.empty"], "nothing is sent without a wish");
        fake.Requests.ShouldBeEmpty();

        page.AiInstruction = "Also tell the journey on the road there.";
        await page.SendAiEditAsync();

        var request = fake.Requests.Single();
        fake.Models.Single().Id.ShouldBe("claude-sonnet-5-5", "the concept step's model");
        fake.KeysUsed.Single().ShouldBe(Secret);
        request.User.ShouldContain("CHANGE: «Also tell the journey on the road there.»");
        request.User.ShouldContain("TITLE: «Ship graffiti on the marble of Hagia Sophia»");
        request.User.ShouldContain("[pause 1 s] Sailors carved their ships into it.", Case.Sensitive, "the user's placeholders go along");
        request.System.ShouldContain("CHANNEL: «Byzantium»");
        request.System.ShouldContain("Keep every one of them exactly as written");
        request.Stream.ShouldBeTrue();
        request.Progress.ShouldNotBeNull();
        request.Effort.ShouldBe(page.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Concepts)!.Effort?.Value ?? string.Empty);
        request.MaxOutputTokens.ShouldBe(ConceptOutputBudget.MaxOutputTokens(fake.Models.Single()));

        // The result opens in the editor first; nothing is saved yet.
        var run = await RunOfAsync(h);
        run.Status.ShouldBe(RunStatus.Succeeded);
        run.ActualUsd.ShouldBeGreaterThan(0);
        page.AiEditing.ShouldBeFalse();
        page.Editing.ShouldBeTrue();
        page.EditHeadline.ShouldBe(other.Title);
        page.EditText.ShouldStartWith("[on screen: road map from Galata]");
        page.EditInfo.ShouldBe(text.Format("concepts.ai.ready", text.Usd(run.ActualUsd)), "the ready line is plain, not a warning");
        page.EditNotice.ShouldContain("#" + 2, Case.Sensitive, "the new title looks like concept #2 (made into a video): a note, not a block");
        page.EditNotice.ShouldNotContain(text.Usd(run.ActualUsd));
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Title.ShouldBe(concept.Title);

        await page.SaveEditAsync();

        var saved = (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!;
        saved.Title.ShouldBe(other.Title);
        saved.Summary.ShouldBe(other.Summary);
        saved.Content.ShouldStartWith("[on screen: road map from Galata]");
        saved.Status.ShouldBe(ConceptStatus.New);
        saved.Number.ShouldBe(1);
        (await h.Get<IEmbeddingStore>().ListAsync(profile.Id, "m", CancellationToken.None)).ShouldBeEmpty("the vectors of the old title and summary are dropped");
        card.Title.ShouldBe("#1 " + other.Title);
        page.Editing.ShouldBeFalse();
        page.Notice.ShouldContain("#2", Case.Sensitive);
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
        await page.AiEditAsync(page.NewConcepts.Single());
        page.AiInstruction = "Shorter.";

        await page.SendAiEditAsync();

        var cut = await RunOfAsync(h);
        cut.Status.ShouldBe(RunStatus.Failed);
        cut.Error.ShouldBe("truncated");
        cut.ActualUsd.ShouldBeGreaterThan(0, "a cut-off answer was billed");
        cut.CostExact.ShouldBeTrue();
        page.AiEditFailed.ShouldBeTrue();
        page.AiEditStatus.ShouldBe(text.Format("concepts.ai.failed", ProviderErrors.Text(text, "truncated"), text.Usd(cut.ActualUsd)));
        page.AiEditing.ShouldBeTrue("the box stays open");
        page.Editing.ShouldBeFalse();

        await page.SendAiEditAsync();

        var unreadable = await RunOfAsync(h);
        unreadable.Error.ShouldBe("parse");
        page.AiEditStatus.ShouldBe(text.Format("concepts.ai.failed", text["concepts.parse_failed"], text.Usd(unreadable.ActualUsd)));
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
        await page.AiEditAsync(page.Approved.Single());
        page.AiInstruction = "Add the road there.";

        var send = page.SendAiEditAsync();
        await Until(() => fake.Requests.Count == 1);
        page.AiEditRunning.ShouldBeTrue();
        page.Working.ShouldBeTrue();
        page.CancelAiEdit();
        await send;

        var run = await RunOfAsync(h);
        run.Status.ShouldBe(RunStatus.Canceled);
        run.CostExact.ShouldBeFalse();
        run.ActualUsd.ShouldBeGreaterThan(0, "the prompt may be billed");
        page.AiEditStatus.ShouldBe(text.Format("concepts.ai.canceled", text.Usd(run.ActualUsd)));
        page.AiEditFailed.ShouldBeFalse();
        page.AiEditRunning.ShouldBeFalse();
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Content.ShouldBe(Content);

        // Cancel again closes the box.
        page.CancelAiEdit();
        page.AiEditing.ShouldBeFalse();
        page.Listing.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task UnsavedEditorTextIsNotLostWhenTheAiBoxIsCanceledOrAnotherConceptIsOpened()
    {
        using var h = new DesktopHarness();
        var (profile, concept) = await PrepareAsync(h);
        var second = new Concept { ProfileId = profile.Id, Title = "The Nika riot", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Approved };
        await h.Get<IConceptStore>().AddAsync([second], CancellationToken.None);
        var text = h.Get<Localizer>();
        var page = await PageAsync(h);
        var card = page.NewConcepts.Single();
        var approved = page.Approved.Single();

        await page.EditAsync(card);
        page.EditHeadline = "My own title";
        page.EditText = Content + "\n[pause 2 s] My own line.";

        // Another concept does not open over the unsaved changes.
        await page.EditAsync(approved);
        await page.AiEditAsync(approved);
        page.EditCard.ShouldBe(card);
        page.AiEditing.ShouldBeFalse();
        page.EditStatus.ShouldBe(text["concepts.unsaved"]);

        // ✨ from the editor, then Cancel: back to the editor with the typed text.
        await page.AiEditAsync(card);
        page.AiEditing.ShouldBeTrue();
        page.Editing.ShouldBeFalse();
        page.CancelAiEdit();
        page.AiEditing.ShouldBeFalse();
        page.EditCard.ShouldBe(card);
        page.EditHeadline.ShouldBe("My own title");
        page.EditText.ShouldEndWith("My own line.");

        // A typed wish in the AI box is not cleared by another concept's ✨.
        page.CancelEdit();
        await page.AiEditAsync(card);
        page.AiInstruction = "Add the road there.";
        await page.AiEditAsync(approved);
        page.AiEditCard.ShouldBe(card);
        page.AiInstruction.ShouldBe("Add the road there.");
        page.AiEditStatus.ShouldBe(text["concepts.unsaved"]);
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Title.ShouldBe(concept.Title);
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
        await page.AiEditAsync(page.NewConcepts.Single());
        page.AiInstruction = "Shorter.";
        var send = page.SendAiEditAsync();
        await Until(() => fake.Requests.Count == 1);

        var otherProfile = new Profile { Name = "Rome", ContentLanguage = ContentLanguages.English, ConceptText = "Roman history." };
        await h.Get<IProfileStore>().SaveAsync(otherProfile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(otherProfile, CancellationToken.None);
        await page.ProfileChangedAsync();
        gate.SetResult();
        await send;

        page.Editing.ShouldBeFalse("the old profile's concept does not open on the new profile's page");
        page.AiEditing.ShouldBeFalse();
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Title.ShouldBe(concept.Title);
    }

    [AvaloniaFact]
    public async Task AQueuedConceptIsNotEditedWithAi()
    {
        using var h = new DesktopHarness();
        var (_, concept) = await PrepareAsync(h);
        var page = await PageAsync(h);
        var card = page.NewConcepts.Single();
        await h.Get<IConceptStore>().SetStatusAsync([concept.Id], ConceptStatus.Queued, CancellationToken.None);

        await page.AiEditAsync(card);

        page.AiEditing.ShouldBeFalse();
        page.Status.ShouldBe(h.Get<Localizer>()["concepts.ai.not_editable"]);
    }
}
