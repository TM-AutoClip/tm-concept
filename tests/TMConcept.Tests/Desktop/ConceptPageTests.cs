using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Keys;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;
using TMConcept.Core.Runs;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The concept page of 0.1.13: the shared bar, section A (suggested topics, one concept per ticked topic, no extra request), section B (an
/// idea and a count, every press adds), one paid call at a time, the New / Approved list and the detail panel. Fake models only.
/// </summary>
public sealed class ConceptPageTests
{
    private const string Secret = "fake-test-key-0123456789abcdef";

    private static async Task<Profile> PrepareAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "Byzantine history." };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "mine", Secret = Secret }, CancellationToken.None);
        return profile;
    }

    /// <summary>
    /// An earlier run (outside the tests' "last hour") that measured <paramref name="perConcept"/> output tokens per concept: short concepts,
    /// so a batch of 20 fits.
    /// </summary>
    private static Task MeasuredAsync(DesktopHarness h, Profile profile, int perConcept = 1_000) => h.Get<IRunStore>().AddAsync(new Run
    {
        ProfileId = profile.Id,
        Kind = RunKind.Concepts,
        Status = RunStatus.Succeeded,
        Accepted = 20,
        OutputTokens = 20 * perConcept,
        StartedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
        FinishedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
    }, CancellationToken.None);

    private static async Task<ConceptsViewModel> PageAsync(DesktopHarness h)
    {
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();
        var row = page.Settings.Picker.Row(PipelineSteps.Concepts)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        return page;
    }

    private static void TopicsModel(ConceptsViewModel page)
    {
        var row = page.Settings.Picker.Row(PipelineSteps.Topics)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
    }

    private static ChatResponse TopicAnswer(IEnumerable<string> topics) => new(
        "{\"scope\": \"Byzantine history\", \"topics\": [" + string.Join(", ", topics.Select(t => System.Text.Json.JsonSerializer.Serialize(t))) + "]}",
        new ChatUsage(600, 0, 0, 120), null, "end_turn", "r");

    private static List<string> Numbered(int from, int count) => Enumerable.Range(from, count).Select(i => "Topic number " + i).ToList();

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 150 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        condition().ShouldBeTrue();
    }

    /// <summary>The page in a window of the page area of the 1280×720 main window (the app bar and the folded log take about 74 px).</summary>
    private static (Window Window, TMConcept.Desktop.Views.ConceptsView View) Show(DesktopHarness h, ConceptsViewModel page, int width = 1280, int height = 646)
    {
        var view = new TMConcept.Desktop.Views.ConceptsView { DataContext = page };
        var window = new Window { Content = view, Width = width, Height = height };
        h.Get<LanguageResources>().Attach(window.Resources.MergedDictionaries);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static T Named<T>(Visual root, string name)
        where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(new Point(0, 0), root) ?? throw new InvalidOperationException(control.Name), control.Bounds.Size);

    [AvaloniaFact]
    public async Task PageAt1280x720ShowsBothSectionsListAndDetailWithoutOverlap()
    {
        using var h = new DesktopHarness();
        await PrepareAsync(h);
        h.Get<Localizer>().Use("tr");
        var page = await PageAsync(h);
        var (window, view) = Show(h, page);

        var area = new Rect(0, 0, view.Bounds.Width, view.Bounds.Height);
        var parts = new[] { "PageBar", "TopicSection", "IdeaSection", "NewList", "Detail" }.Select(n => (Name: n, Rect: BoundsIn(Named<Control>(view, n), view))).ToList();
        foreach (var (name, rect) in parts)
        {
            rect.Width.ShouldBeGreaterThan(100, name);
            rect.Height.ShouldBeGreaterThan(30, name);
            area.Contains(rect).ShouldBeTrue($"{name} {rect} lies inside the page {area}");
        }

        foreach (var a in parts)
        {
            foreach (var b in parts.Where(b => string.CompareOrdinal(a.Name, b.Name) < 0))
            {
                a.Rect.Intersects(b.Rect).ShouldBeFalse($"{a.Name} {a.Rect} and {b.Name} {b.Rect} do not overlap");
            }
        }

        BoundsIn(Named<Control>(view, "TopicSection"), view).Y.ShouldBe(BoundsIn(Named<Control>(view, "IdeaSection"), view).Y, "the two sections stand side by side");
        Named<Control>(view, "TopicSection").GetVisualAncestors().OfType<ScrollViewer>().ShouldBeEmpty("the page itself does not scroll");
        Named<Control>(view, "Detail").GetVisualAncestors().OfType<ScrollViewer>().ShouldBeEmpty();
        Named<Button>(Named<Control>(view, "TopicFooter"), "GenerateButton").IsEffectivelyVisible.ShouldBeTrue();
        Named<Button>(Named<Control>(view, "IdeaFooter"), "GenerateButton").IsEffectivelyVisible.ShouldBeTrue();
        window.Close();
    }

    [AvaloniaFact]
    public async Task ModelBarIsFoldedAndShowsTheSummaryLine()
    {
        using var h = new DesktopHarness();
        var profile = await PrepareAsync(h);
        var text = h.Get<Localizer>();
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();

        // A new profile has no concept model: the models unfold by themselves, so the first thing to do is in sight.
        page.Settings.ModelsOpen.ShouldBeTrue();
        page.Settings.Picker.Rows.Select(r => r.Step.Code).ShouldBe([PipelineSteps.Concepts, PipelineSteps.Embedding, PipelineSteps.Topics]);
        page.Settings.Picker.Compact.ShouldBeTrue();
        page.Settings.Picker.Columns.ShouldBe(3);

        var concepts = page.Settings.Picker.Row(PipelineSteps.Concepts)!;
        concepts.Selected = concepts.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        await page.Settings.Picker.SaveAsync();
        page.Settings.Picker.Summary.ShouldContain(text["steps.short.concepts"] + ": " + concepts.Selected!.Model!.Name);
        page.Settings.Picker.Summary.ShouldContain(text["steps.short.embedding"] + ": " + text["steps.off"], Case.Sensitive, "an optional step without a model is off");
        page.Settings.Picker.Summary.ShouldContain(text["steps.short.topics"] + ": " + text["steps.off"]);

        // Another visit with a concept model chosen: everything stays folded and the line names the choices.
        page.Settings.ModelsOpen = false;
        await page.ActivateAsync();
        page.Settings.ModelsOpen.ShouldBeFalse();
        var (window, view) = Show(h, page);
        var bar = Named<TMConcept.Desktop.Views.ModelSummaryBar>(view, "ModelsBar");
        bar.IsOpen.ShouldBeFalse();
        Named<TextBlock>(bar, "SummaryLine").Text.ShouldBe(page.Settings.Picker.Summary);
        Named<TMConcept.Desktop.Views.StepPickerView>(view, "ModelPickers").IsVisible.ShouldBeFalse();
        Named<ItemsControl>(view, "FineTuneRows").IsVisible.ShouldBeFalse("fine-tuning is folded too");
        page.Settings.ModelsOpen = true;
        Dispatcher.UIThread.RunJobs();
        Named<TMConcept.Desktop.Views.StepPickerView>(view, "ModelPickers").IsVisible.ShouldBeTrue();
        (await h.Get<TMConcept.Core.Models.IStepModelStore>().ListAsync(profile.Id, CancellationToken.None)).ShouldContain(c => c.Step == PipelineSteps.Concepts);

        // The long explanations are "?" tips, not lines that are always open.
        var shown = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        foreach (var key in new[] { "concepts.check.hint", "focus.hint", "concepts.topics.tip", "concepts.idea.tip", "step.concepts.purpose", "topics.hint" })
        {
            shown.ShouldNotContain(text[key], key);
        }

        window.Close();
    }

    [AvaloniaFact]
    public async Task OnlyGeneralFineTuningIsAskedAndItGoesIntoThePromptInTheVideosLanguage()
    {
        var fake = new FakeChat().Concepts(FakeChat.Distinct(3));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var profile = await PrepareAsync(h);
        profile.TemplateId = "history.byzantine";
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        h.Get<Localizer>().Use("tr");
        var page = await PageAsync(h);
        await page.ProfileChangedAsync();
        var row = page.Settings.Picker.Row(PipelineSteps.Concepts)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");

        // Nothing asks the user to know the subject (period, region, field…), whatever the profile's template; every choice starts on "any".
        h.Get<IFocusSource>().Dimensions.Select(d => d.Id).ShouldBe(["tone", "audience", "structure"]);
        var rows = page.Settings.FocusRows;
        rows.Select(r => r.Dimension.Id).ShouldBe(["tone", "audience", "structure"]);
        rows.ShouldAllBe(r => r.Chosen == null);
        rows[0].Title.ShouldBe("Ton", "the drop-downs speak the interface language");
        rows[0].Options[0].Label.ShouldBe("fark etmez");
        page.Settings.FineTuneSummary.ShouldStartWith("Ton: seçilmedi · ");
        rows[0].Selected = rows[0].Options.First(o => o.Value == "mysterious");
        page.Settings.FineTuneSummary.ShouldStartWith("Ton: gizemli · ", Case.Sensitive, "the folded line tells what is chosen");
        page.Ideas.Count = 3;

        await page.Ideas.GenerateAsync();

        var user = fake.Requests.Single().User;
        user.ShouldContain("FOCUS (the tone, audience and structure the user chose; follow it): Tone: mysterious", Case.Sensitive,
            "the profile writes English videos: the focus line is English");
        user.ShouldNotContain("Period");
        user.ShouldNotContain("CATEGORY");

        // Switching tabs keeps the choice; another profile starts on "any".
        await page.ActivateAsync();
        page.Settings.FocusRows[0].Chosen.ShouldNotBeNull();
        await page.ProfileChangedAsync();
        page.Settings.FocusRows.ShouldAllBe(r => r.Chosen == null);
    }

    [AvaloniaFact]
    public async Task FineTuneFocusGoesToSuggestTopicsAndBothSections()
    {
        var fake = new FakeChat()
            .Answer(_ => TopicAnswer(["Greek fire", "The Nika riots"]))
            .Concepts(FakeChat.Distinct(1))
            .Concepts(FakeChat.Distinct(1));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        TopicsModel(page);
        var tone = page.Settings.FocusRows.First(r => r.Dimension.Id == "tone");
        tone.Selected = tone.Options.First(o => o.Value == "mysterious");

        await page.Topics.SuggestAsync();
        page.Topics.Topics[0].Checked = true;
        await page.Topics.GenerateAsync();
        page.Ideas.Count = 1;
        await page.Ideas.GenerateAsync();

        fake.Requests.Count.ShouldBe(3);
        fake.Requests[0].User.ShouldContain("FOCUS: Tone: mysterious", Case.Sensitive, "topic suggestions use the shared fine-tuning");
        fake.Requests[1].User.ShouldContain("Tone: mysterious", Case.Sensitive, "section A");
        fake.Requests[2].User.ShouldContain("Tone: mysterious", Case.Sensitive, "section B");
    }

    [AvaloniaFact]
    public async Task ACanceledTopicCallIsKeptWithTheEstimatedPromptCostAndTheLowestEffortIsUsed()
    {
        var fake = new FakeChat().Answer(_ => throw new OperationCanceledException());
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var profile = await PrepareAsync(h);
        var haiku = h.Get<TMConcept.Core.Models.IModelCatalog>().Find("anthropic", "claude-haiku-5-5")!;
        var key = (await h.Get<IApiKeyStore>().ListAsync("anthropic", CancellationToken.None)).First();
        var model = new TMConcept.Core.Models.ResolvedModel(haiku, key, string.Empty);

        // Awaited directly: a canceled task gives back the exception it was canceled with (Shouldly's ThrowAsync would see a new one).
        TopicsCanceledException? canceled = null;
        try
        {
            await h.Get<TopicSuggester>().SuggestAsync(profile, model, string.Empty, TopicLimits.CountDefault, "en", null, CancellationToken.None);
        }
        catch (TopicsCanceledException ex)
        {
            canceled = ex;
        }

        canceled.ShouldNotBeNull();

        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Single(r => r.Kind == RunKind.Topics);
        run.Status.ShouldBe(RunStatus.Canceled);
        run.CostExact.ShouldBeFalse();
        run.ActualUsd.ShouldBeGreaterThan(0, "the prompt may be billed: it is counted in the month's spending");
        canceled.EstimatedUsd.ShouldBe(run.ActualUsd);

        TopicSuggester.EffortFor(new TMConcept.Core.Models.CatalogModel { Efforts = ["minimal", "medium", "high"] }).ShouldBe("minimal");
        TopicSuggester.EffortFor(new TMConcept.Core.Models.CatalogModel { Efforts = ["medium", "high"] }).ShouldBe("medium");
        TopicSuggester.EffortFor(haiku).ShouldBe(TopicSuggester.Effort);
        TopicSuggester.EffortFor(new TMConcept.Core.Models.CatalogModel()).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task SuggestTopicsListsCheckboxesWithTickAllAndNone()
    {
        var fake = new FakeChat().Answer(_ => new ChatResponse(
            "{\"scope\": \"Byzantine history\", \"topics\": [\"The fall of Trebizond\", \"Greek fire\", \"greek  fire\", \"\"]}",
            new ChatUsage(600, 0, 0, 120), null, "end_turn", "r"));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var profile = await PrepareAsync(h);
        await h.Get<IConceptStore>().AddAsync([new Concept { ProfileId = profile.Id, Title = "Hagia Sophia", Summary = "s", Content = "c", Language = "en" }],
            CancellationToken.None);
        var page = await PageAsync(h);
        var text = h.Get<Localizer>();
        var section = page.Topics;
        await section.SuggestAsync();
        section.SuggestStatus.ShouldBe(text["topics.no_model"], "nothing is asked without a model");
        section.SuggestFailed.ShouldBeTrue();
        fake.Requests.ShouldBeEmpty();

        TopicsModel(page);
        await Until(() => section.SuggestEstimateDetail.Contains("Claude Haiku", StringComparison.Ordinal));
        section.SuggestEstimate.ShouldStartWith("≈ ");
        page.Ideas.Idea = "only sieges";
        section.TopicCount = 5;

        await section.SuggestAsync();

        section.Topics.Select(t => t.Text).ShouldBe(["The fall of Trebizond", "Greek fire"], "cleaned, no repeats, no empty ones");
        section.Topics.ShouldAllBe(t => !t.Checked, "suggestions come unticked");
        section.CheckedText.ShouldBe(text.Format("concepts.topics.checked", 0, 2));
        section.CanGenerate.ShouldBeFalse();
        section.Blocked.ShouldBe(text["concepts.topics.none"], "the button says why it is off");
        var request = fake.Requests.Single();
        request.User.ShouldContain("- Hagia Sophia", Case.Sensitive, "earlier titles are not suggested again");
        request.User.ShouldContain("Suggest 5 topics now.", Case.Sensitive, "the count picker's value is asked for");
        request.User.ShouldNotContain("only sieges", Case.Sensitive, "section B's idea is not part of the topics prompt");
        request.User.ShouldNotContain("EXTRA REQUEST");
        request.MaxOutputTokens.ShouldBe(TopicSuggester.OutputMax, "room for thinking (1 500 cut Haiku at effort high before the first topic)");
        request.Stream.ShouldBeTrue();
        request.Effort.ShouldBe(TopicSuggester.Effort, "a cheap, quick call: always effort low");
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Single(r => r.Kind == RunKind.Topics);
        section.SuggestStatus.ShouldContain(text.Usd(run.ActualUsd));

        section.TickAll();
        section.CheckedCount.ShouldBe(2);
        section.GenerateText.ShouldBe(text.Format("concepts.topics.generate", 2));
        section.CanGenerate.ShouldBeTrue();
        section.TickNone();
        section.CheckedCount.ShouldBe(0);
        section.Topics[1].Checked = true;
        section.CheckedText.ShouldBe(text.Format("concepts.topics.checked", 1, 2));
        await Until(() => section.Estimate.StartsWith("≈ ", StringComparison.Ordinal));
        page.Ideas.Idea.ShouldBe("only sieges", "ticking does not touch section B");
    }

    [AvaloniaFact]
    public async Task TheTopicCountIsKeptWithinFiveToAHundredAndTheEstimateFollowsIt()
    {
        var fake = new FakeChat().Answer(_ => TopicAnswer(["Greek fire"])).Answer(_ => TopicAnswer(["The Nika riots"]));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        TopicsModel(page);
        var section = page.Topics;
        await Until(() => section.SuggestEstimateDetail.Contains("Claude Haiku", StringComparison.Ordinal));
        section.TopicCount.ShouldBe(TopicLimits.CountDefault);

        section.TopicCount = 5;
        await Until(() => section.SuggestEstimate.Length > 0);
        var few = section.SuggestEstimateDetail;
        section.TopicCount = 100;
        await Until(() => section.SuggestEstimateDetail != few);

        section.TopicCount = 500;
        await section.SuggestAsync();
        fake.Requests[0].User.ShouldContain("Suggest 100 topics now.", Case.Sensitive, "at most 100");
        section.TopicCount = 1;
        await section.SuggestAsync();
        fake.Requests[1].User.ShouldContain("Suggest 5 topics now.", Case.Sensitive, "at least 5");
    }

    [AvaloniaFact]
    public async Task SuggestingAgainKeepsTheTickedTopicsAndReplacesTheRest()
    {
        var fake = new FakeChat()
            .Answer(_ => TopicAnswer(["The fall of Trebizond", "Greek fire", "The Nika riots"]))
            .Answer(_ => TopicAnswer(["Basil II", "The Varangian Guard"]));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        TopicsModel(page);
        var section = page.Topics;

        await section.SuggestAsync();
        section.Topics[2].Checked = true;
        section.Topics[0].Checked = true;
        await section.SuggestAsync();

        section.Topics.Select(t => t.Text).ShouldBe(["The fall of Trebizond", "The Nika riots", "Basil II", "The Varangian Guard"],
            "the ticked ones stay on top in their order, the unticked one is gone, the new ones come below");
        section.Topics.Select(t => t.Checked).ShouldBe([true, true, false, false]);
        fake.Requests[1].User.ShouldContain("- Greek fire", Case.Sensitive, "a dropped suggestion is still remembered and not suggested again");
    }

    [AvaloniaFact]
    public async Task GenerateSelectedWritesExactlyOneConceptPerTickedTopicAndNoExtraRequest()
    {
        var topics = Numbered(1, 20);
        var fake = new FakeChat().Answer(_ => TopicAnswer(topics)).Concepts(FakeChat.Distinct(8));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        TopicsModel(page);
        var section = page.Topics;
        await section.SuggestAsync();
        var ticked = new[] { 0, 2, 3, 6, 9, 11, 15, 19 };
        foreach (var i in ticked)
        {
            section.Topics[i].Checked = true;
        }

        page.Ideas.Idea = "five concepts about sieges";
        page.Ideas.Count = 20;
        section.GenerateText.ShouldBe(h.Get<Localizer>().Format("concepts.topics.generate", 8));

        await section.GenerateAsync();

        var user = fake.Requests[1].User;
        user.ShouldContain("TOPICS (one concept each, in order):\n" + string.Join("\n", ticked.Select((t, n) => $"{n + 1}. «{topics[t]}»")), Case.Sensitive);
        user.ShouldContain("REQUESTED: 8 new concepts.", Case.Sensitive, "8 ticked of 20: exactly 8, not the idea section's 20");
        user.ShouldNotContain("EXTRA REQUEST", Case.Sensitive, "the idea box is never part of a topic run");
        user.ShouldNotContain("sieges");
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Single(r => r.Kind == RunKind.Concepts);
        run.Requested.ShouldBe(8);
        run.ExtraPrompt.ShouldBeEmpty();
        page.NewConcepts.Count.ShouldBe(8);
        section.Topics.Count.ShouldBe(12, "the 8 made topics left the list");
        section.Topics.ShouldAllBe(t => !t.Checked);
        section.CanGenerate.ShouldBeFalse("nothing is ticked any more");
        section.Blocked.ShouldBe(h.Get<Localizer>()["concepts.topics.none"]);
        section.IsBlocked.ShouldBeTrue("the reason is shown again once the run is over");
        section.Status.ShouldBe(h.Get<Localizer>().Format("concepts.done", 8, h.Get<Localizer>().Usd(run.ActualUsd)));
    }

    [AvaloniaFact]
    public async Task ACutTopicRunLeavesTheUnreachedTopicsTickedAndASecondPressMakesOnlyThem()
    {
        var titles = FakeChat.Distinct(5);
        var full = FakeChat.Json(titles);
        var starts = new List<int>();
        for (var at = full.IndexOf("{\"baslik\"", StringComparison.Ordinal); at >= 0; at = full.IndexOf("{\"baslik\"", at + 1, StringComparison.Ordinal))
        {
            starts.Add(at);
        }

        var topics = Numbered(1, 5);
        var fake = new FakeChat()
            .Answer(_ => TopicAnswer(topics))
            .Answer(_ => throw new ProviderException("cut")
            {
                Code = "truncated",
                ChargedUsage = new ChatUsage(1000, 0, 0, 30_000),
                PartialText = full[..(starts[3] + 12)],
                StopReason = "max_tokens",
            })
            .Concepts(titles.Skip(3));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        TopicsModel(page);
        var section = page.Topics;
        await section.SuggestAsync();
        section.TickAll();

        await section.GenerateAsync();

        page.NewConcepts.Count.ShouldBe(3);
        section.Topics.Select(t => t.Text).ShouldBe(topics.Skip(3), "the topics after the cut stay");
        section.Topics.ShouldAllBe(t => t.Checked, "and stay ticked");
        var text = h.Get<Localizer>();
        section.Status.ShouldStartWith(text.Format("concepts.topics.left", 3, 5, 2));
        section.StatusIsWarning.ShouldBeTrue();

        await section.GenerateAsync();

        fake.Requests.Count.ShouldBe(3);
        fake.Requests[2].User.ShouldContain("TOPICS (one concept each, in order):\n1. «Topic number 4»\n2. «Topic number 5»", Case.Sensitive);
        fake.Requests[2].User.ShouldContain("REQUESTED: 2 new concepts.", Case.Sensitive);
        page.NewConcepts.Count.ShouldBe(5);
        section.Topics.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task MoreThan100TickedTurnsGenerateOffWithTheReason()
    {
        var fake = new FakeChat().Answer(_ => TopicAnswer(Numbered(1, 70))).Answer(_ => TopicAnswer(Numbered(71, 40)));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        TopicsModel(page);
        var section = page.Topics;
        section.TopicCount = 70;
        await section.SuggestAsync();
        section.TickAll();
        await section.SuggestAsync();
        section.TickAll();

        section.CheckedCount.ShouldBe(110);
        section.CanGenerate.ShouldBeFalse();
        section.Blocked.ShouldBe(h.Get<Localizer>().Format("concepts.topics.over", 110), "never cut silently: the button says why");
        await section.GenerateAsync();
        fake.Requests.Count.ShouldBe(2, "nothing was sent");

        foreach (var topic in section.Topics.Skip(100))
        {
            topic.Checked = false;
        }

        section.CanGenerate.ShouldBeTrue("100 is allowed");
    }

    [AvaloniaFact]
    public async Task GenerateIdeaAddsConceptsAndNeverSendsTopics()
    {
        var fake = new FakeChat().Answer(_ => TopicAnswer(["Greek fire", "The Nika riots"])).Concepts(FakeChat.Distinct(3));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        TopicsModel(page);
        await page.Topics.SuggestAsync();
        page.Topics.TickAll();
        page.Ideas.Idea = "The last days of Byzantium";
        page.Ideas.Count = 3;

        await page.Ideas.GenerateAsync();

        var user = fake.Requests[1].User;
        user.ShouldContain("EXTRA REQUEST: «The last days of Byzantium»");
        user.ShouldContain("REQUESTED: 3 new concepts.");
        user.ShouldNotContain("TOPICS (one concept each", Case.Sensitive, "ticked topics belong to section A only");
        page.NewConcepts.Count.ShouldBe(3);
        page.Topics.Topics.Count.ShouldBe(2, "section A's list did not change");
        page.Topics.CheckedCount.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task IdeaPressesAppendToTheListAndDoNotClearIt()
    {
        var fake = new FakeChat().Concepts(["Greek fire"]).Concepts(["The Nika riots"]);
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        page.Ideas.Count = 1;

        await page.Ideas.GenerateAsync();
        var first = page.NewConcepts.Single();
        page.Ideas.Idea = "One more on riots";
        await page.Ideas.GenerateAsync();

        page.NewConcepts.Count.ShouldBe(2);
        page.NewConcepts.ShouldContain(first, "the earlier concept stays");
        page.NewHeader.ShouldBe(h.Get<Localizer>().Format("concepts.list.new", 2));
    }

    [AvaloniaFact]
    public async Task AnEmptyIdeaCountTurnsGenerateOffWithTheReasonAndSendsNothing()
    {
        var fake = new FakeChat().Concepts(["Greek fire"]);
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);

        page.Ideas.Count = null;
        await page.Ideas.GenerateAsync();

        fake.Requests.ShouldBeEmpty("an emptied count box is not silently one concept");
        page.Ideas.CanGenerate.ShouldBeFalse();
        page.Ideas.Blocked.ShouldBe(h.Get<Localizer>()["concepts.idea.count_empty"]);
        page.Ideas.IsBlocked.ShouldBeTrue();
        page.Ideas.Count = 1;
        page.Ideas.CanGenerate.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task ATopicUntickedDuringTheRunStillLeavesTheListOnceItHasItsConcept()
    {
        var gate = new TaskCompletionSource();
        var topics = Numbered(1, 3);
        var fake = new FakeChat().Answer(_ => TopicAnswer(topics)).AnswerAsync(async _ =>
        {
            await gate.Task;
            return new ChatResponse(FakeChat.Json(FakeChat.Distinct(2)), new ChatUsage(1000, 0, 0, 2000), null, "end_turn", "r");
        });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        TopicsModel(page);
        var section = page.Topics;
        await section.SuggestAsync();
        section.Topics[0].Checked = true;
        section.Topics[1].Checked = true;

        var run = section.GenerateAsync();
        await Until(() => fake.Requests.Count == 2);
        section.TickNone();
        gate.SetResult();
        await run;

        page.NewConcepts.Count.ShouldBe(2);
        section.Topics.Select(t => t.Text).ShouldBe([topics[2]], "both made topics left the list although they were unticked meanwhile");
    }

    [AvaloniaFact]
    public async Task OneRunAtATimeTheOtherSectionShowsWhy()
    {
        var gate = new TaskCompletionSource();
        var fake = new FakeChat().AnswerAsync(async _ =>
        {
            await gate.Task;
            return new ChatResponse(FakeChat.Json(FakeChat.Distinct(1)), new ChatUsage(1000, 0, 0, 2000), null, "end_turn", "r");
        });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        TopicsModel(page);
        var text = h.Get<Localizer>();
        page.Ideas.Count = 1;

        var run = page.Ideas.GenerateAsync();
        await Until(() => fake.Requests.Count == 1);

        page.Runner.Busy.ShouldBeTrue();
        page.Working.ShouldBeTrue("the profile cannot be switched meanwhile");
        page.Ideas.Running.ShouldBeTrue();
        page.Topics.CanSuggest.ShouldBeFalse();
        page.Topics.Blocked.ShouldBe(text["concepts.busy_other"]);
        page.Topics.CanGenerate.ShouldBeFalse();
        page.Ideas.CanGenerate.ShouldBeFalse("its own run goes on (Cancel is shown instead)");
        await page.Topics.SuggestAsync();
        fake.Requests.Count.ShouldBe(1, "nothing else is sent while one call runs");

        gate.SetResult();
        await run;
        page.Runner.Busy.ShouldBeFalse();
        page.Topics.Blocked.ShouldNotBe(text["concepts.busy_other"]);
        page.Topics.CanSuggest.ShouldBeTrue();
        page.Working.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task SuggestTopicsKnowsEveryConceptAndEveryEarlierSuggestionAndDropsRepeats()
    {
        static ChatResponse Answer(string json) => new(json, new ChatUsage(600, 0, 0, 120), null, "end_turn", "r");
        var fake = new FakeChat()
            .Answer(_ => Answer("{\"topics\": [\"The fall of Trebizond\", \"Greek fire\"]}"))
            .Answer(_ => Answer("{\"topics\": [\"greek FIRE\", \"Hagia  Sophia\", \"The Varangian Guard\", \"Theodora's riot\"]}"))
            .Answer(_ => Answer("{\"topics\": [\"The Varangian guard\", \"Greek Fire\"]}"));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var profile = await PrepareAsync(h);
        var store = h.Get<IConceptStore>();
        Concept Of(string title) => new() { ProfileId = profile.Id, Title = title, Summary = "s", Content = "c", Language = "en" };
        Concept produced = Of("Hagia Sophia"), trashed = Of("Theodora's riot"), fresh = Of("The Nika riot");
        await store.AddAsync([produced, trashed, fresh], CancellationToken.None);
        await store.SetStatusAsync([produced.Id], ConceptStatus.Produced, CancellationToken.None);
        await store.SetStatusAsync([trashed.Id], ConceptStatus.Trashed, CancellationToken.None);
        var page = await PageAsync(h);
        TopicsModel(page);
        var section = page.Topics;

        await section.SuggestAsync();
        section.Topics.Select(t => t.Text).ShouldBe(["The fall of Trebizond", "Greek fire"]);
        var first = fake.Requests.Single().User;
        first.ShouldContain("- [video] Hagia Sophia", Case.Sensitive, "a concept made into a video is marked");
        first.ShouldContain("- Theodora's riot", Case.Sensitive, "a trashed concept is no new topic either");
        first.ShouldContain("- The Nika riot", Case.Sensitive);
        first.ShouldNotContain("ALREADY SUGGESTED");
        var suggestions = h.Get<ITopicSuggestionStore>();
        (await suggestions.ListAsync(profile.Id, 100, CancellationToken.None)).Select(s => s.Text).ShouldBe(["The fall of Trebizond", "Greek fire"],
            ignoreOrder: true);

        await section.SuggestAsync();
        var second = fake.Requests[1].User;
        second.ShouldContain("ALREADY SUGGESTED");
        second.ShouldContain("- The fall of Trebizond", Case.Sensitive);
        second.ShouldContain("- Greek fire", Case.Sensitive);
        section.Topics.Select(t => t.Text).ShouldBe(["The Varangian Guard"], "an earlier suggestion and two concept titles (folded) are left out");
        var text = h.Get<Localizer>();
        section.SuggestStatus.ShouldContain(text.Format("topics.repeats", 3));
        (await suggestions.ListAsync(profile.Id, 100, CancellationToken.None)).Count.ShouldBe(3, "only the kept ones are saved");
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None))
            .Where(r => r.Kind == RunKind.Topics).OrderBy(r => r.StartedAtUtc).Last();
        run.Accepted.ShouldBe(1);

        await section.SuggestAsync();
        section.Topics.ShouldBeEmpty();
        section.SuggestStatus.ShouldStartWith(text.Format("topics.all_repeats", string.Empty).TrimEnd(' ', '·'));
        var repeated = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None))
            .Where(r => r.Kind == RunKind.Topics).OrderBy(r => r.StartedAtUtc).Last();
        repeated.Status.ShouldBe(RunStatus.Succeeded, "the answer was read; every topic was only a repeat");
        repeated.Error.ShouldBeEmpty();
        repeated.Accepted.ShouldBe(0);
    }

    [Fact]
    public void TheWordCheckAndTheMeaningCheckAreTwoDifferentChecksAndSayIt()
    {
        using var h = new DesktopHarness();
        var text = h.Get<Localizer>();
        foreach (var language in new[] { "tr", "en" })
        {
            text.Use(language);
            text["concepts.check.hint"].ShouldContain(language == "tr" ? "kelimelerini" : "words");
            text["step.embedding.purpose"].ShouldContain(language == "tr" ? "anlamca" : "by meaning");
            text["step.embedding.off.note"].ShouldNotContain(language == "tr" ? "kelime kelime karşılaştırılır" : "still compared word by word");
        }
    }

    [AvaloniaFact]
    public async Task GenerateInBatchesThrowsAwayDuplicatesAndOffersToFillTheMissing()
    {
        var first = FakeChat.Distinct(20);
        var fake = new FakeChat()
            .Concepts(first)
            .Concepts([.. FakeChat.Distinct(4), first[2]]);
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var profile = await PrepareAsync(h);
        await MeasuredAsync(h, profile);
        var page = await PageAsync(h);
        page.Ideas.Count = 25;
        await Until(() => page.Ideas.EstimateDetail.Contains("Claude Sonnet 5.5", StringComparison.Ordinal));

        await page.Ideas.GenerateAsync();

        fake.Requests.Count.ShouldBe(2);
        fake.KeysUsed.ShouldAllBe(k => k == Secret);
        fake.Requests[0].CacheSystem.ShouldBeTrue();
        fake.Requests[1].User.ShouldContain("- " + first[19]);
        fake.Requests[1].System.ShouldBe(fake.Requests[0].System, "the cached part must not change between batches");
        page.NewConcepts.Count.ShouldBe(24);
        page.Ideas.Missing.ShouldBe(1);
        page.Ideas.HasMissing.ShouldBeTrue();
        var text = h.Get<Localizer>();
        page.Ideas.Status.ShouldContain(text.Format("concepts.duplicates_dropped", 1));
        var numbers = (await h.Get<IConceptStore>().ListAsync(profile.Id, [ConceptStatus.New], false, CancellationToken.None)).Select(c => c.Number).ToList();
        numbers.ShouldBe(Enumerable.Range(1, 24).ToList(), ignoreOrder: true);
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Single();
        run.Status.ShouldBe(RunStatus.Succeeded);
        run.Accepted.ShouldBe(24);
        run.RejectedDuplicates.ShouldBe(1);
        run.ActualUsd.ShouldBe(2 * ((1000 * 2.00m) + (2000 * 10.00m)) / 1_000_000m);
        page.Ideas.Status.ShouldContain(text.Usd(run.ActualUsd));
    }

    [AvaloniaFact]
    public async Task AnErrorKeepsTheFinishedBatchesAndTheBilledCost()
    {
        var fake = new FakeChat()
            .Concepts(FakeChat.Distinct(20))
            .Answer(_ => throw new ProviderException("cut") { Code = "truncated", ChargedUsage = new ChatUsage(1000, 0, 0, 1000) });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await MeasuredAsync(h, await PrepareAsync(h));
        var page = await PageAsync(h);
        page.Ideas.Count = 40;

        await page.Ideas.GenerateAsync();

        page.NewConcepts.Count.ShouldBe(20);
        page.Ideas.Missing.ShouldBe(20);
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Single();
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldBe("truncated");
        run.OutputTokens.ShouldBe(3000);
    }

    [AvaloniaFact]
    public async Task ACutOffBatchKeepsItsCompleteConceptsAndStopsTheRun()
    {
        var full = FakeChat.Json(FakeChat.Distinct(5));
        var starts = new List<int>();
        for (var at = full.IndexOf("{\"baslik\"", StringComparison.Ordinal); at >= 0; at = full.IndexOf("{\"baslik\"", at + 1, StringComparison.Ordinal))
        {
            starts.Add(at);
        }

        var fake = new FakeChat()
            .Answer(_ => throw new ProviderException("cut")
            {
                Code = "truncated",
                ChargedUsage = new ChatUsage(1000, 0, 0, 36_000),
                PartialText = full[..(starts[3] + 12)],
            });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        page.Ideas.Count = 20;

        await page.Ideas.GenerateAsync();

        page.NewConcepts.Count.ShouldBe(3);
        page.Ideas.Missing.ShouldBe(17);
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Single();
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldBe("truncated");
        run.Accepted.ShouldBe(3);
        run.OutputTokens.ShouldBe(36_000);
        var text = h.Get<Localizer>();
        page.Ideas.Status.ShouldBe(text.Format("concepts.cut", 3, 17, text.Usd(run.ActualUsd), string.Empty), "it says how many were saved and how many are missing");
        page.Ideas.StatusIsWarning.ShouldBeTrue("a cut is a warning, not a red error");
        page.Ideas.StatusIsError.ShouldBeFalse();
        fake.Requests.Count.ShouldBe(1, "no extra batch is started without asking");

        // Another profile does not keep this press's lines.
        await page.ProfileChangedAsync();
        page.Ideas.Status.ShouldBeEmpty();
        page.Ideas.Missing.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task AnAnswerThatEndedEarlyKeepsItsCompleteConceptsAndSaysSo()
    {
        var full = FakeChat.Json(FakeChat.Distinct(5));
        var second = full.IndexOf("{\"baslik\"", full.IndexOf("{\"baslik\"", StringComparison.Ordinal) + 1, StringComparison.Ordinal);
        var fake = new FakeChat()
            .Answer(_ => throw new ProviderException("ended")
            {
                Code = "incomplete",
                ChargedUsage = new ChatUsage(1000, 0, 0, 2_000),
                PartialText = full[..(second + 12)],
            });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        page.Ideas.Count = 5;

        await page.Ideas.GenerateAsync();

        page.NewConcepts.Count.ShouldBe(1);
        page.Ideas.Missing.ShouldBe(4);
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Single();
        var text = h.Get<Localizer>();
        page.Ideas.Status.ShouldBe(text.Format("concepts.incomplete", 1, 4, text.Usd(run.ActualUsd), string.Empty));
        page.Ideas.StatusIsError.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task FillMissingRunsTheLastIdeaBrief()
    {
        var full = FakeChat.Json(FakeChat.Distinct(5));
        var cutAt = full.IndexOf("{\"baslik\"", full.IndexOf("{\"baslik\"", full.IndexOf("{\"baslik\"", StringComparison.Ordinal) + 1, StringComparison.Ordinal) + 1,
            StringComparison.Ordinal);
        var fake = new FakeChat()
            .Answer(_ => throw new ProviderException("cut")
            {
                Code = "truncated",
                ChargedUsage = new ChatUsage(1000, 0, 0, 13_000),
                PartialText = full[..(cutAt + 12)],
                StopReason = "max_tokens",
            })
            .Concepts(FakeChat.Distinct(3));
        var confirm = new FakeConfirm { Answer = false };
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IChatClientFactory>(fake);
            s.AddSingleton<IConfirmDialog>(confirm);
        });
        await PrepareAsync(h);
        var page = await PageAsync(h);
        page.Ideas.Count = 5;
        page.Ideas.Idea = "five concepts: one on Hagia Sophia, one on religions";

        await page.Ideas.GenerateAsync();
        page.NewConcepts.Count.ShouldBe(2);
        page.Ideas.Missing.ShouldBe(3);
        fake.Requests[0].MaxOutputTokens.ShouldBe(ConceptOutputBudget.Ceiling, "the model's limit (128 000) capped at 64 000, not a size from the count");

        await page.Ideas.FillMissingAsync();
        confirm.Asked.Count.ShouldBe(1);
        confirm.Messages.Single().ShouldContain("$", Case.Sensitive, "the question shows the estimate");
        fake.Requests.Count.ShouldBe(1, "declined: nothing is asked or paid for");

        // The box was changed meanwhile: "Fill the missing" still runs the idea that ran.
        page.Ideas.Idea = "something else";
        confirm.Answer = true;
        await page.Ideas.FillMissingAsync();
        fake.Requests.Count.ShouldBe(2);
        fake.Requests[1].User.ShouldContain("REQUESTED: 3 new concepts. Write exactly 3 concepts, even when the extra request names another number.");
        fake.Requests[1].User.ShouldContain("EXTRA REQUEST: «five concepts: one on Hagia Sophia, one on religions»");
        page.NewConcepts.Count.ShouldBe(5);
        page.Ideas.Missing.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task ACutOffTopicAnswerStillShowsTheCompleteTopics()
    {
        var fake = new FakeChat().Answer(_ => throw new ProviderException("cut")
        {
            Code = "truncated",
            ChargedUsage = new ChatUsage(600, 0, 0, 16_000),
            PartialText = "{\"topics\": [\"The fall of Trebizond\", \"Greek \\\"fire\\\"\", \"The Nika ri",
            StopReason = "max_tokens",
        });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        TopicsModel(page);

        await page.Topics.SuggestAsync();

        page.Topics.Topics.Select(t => t.Text).ShouldBe(["The fall of Trebizond", "Greek \"fire\""], "the unfinished last topic is dropped");
        var text = h.Get<Localizer>();
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Single(r => r.Kind == RunKind.Topics);
        run.Status.ShouldBe(RunStatus.Succeeded);
        run.OutputTokens.ShouldBe(16_000);
        page.Topics.SuggestStatus.ShouldBe(text.Format("topics.done_cut", 2, text.Usd(run.ActualUsd)));
    }

    [AvaloniaFact]
    public async Task ATopicRequestThatFailsWithoutAProviderErrorStillSaysWhy()
    {
        var fake = new FakeChat().Answer(_ => throw new InvalidOperationException("broken pipe"));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        TopicsModel(page);

        await page.Topics.SuggestAsync();

        page.Topics.SuggestStatus.ShouldContain("broken pipe");
        page.Topics.SuggestFailed.ShouldBeTrue();
        page.Topics.Suggesting.ShouldBeFalse();
        page.Runner.Busy.ShouldBeFalse("the page's call is given back");
    }

    /// <summary>Reports a step of a fake call and waits until the interface thread has handled it (as a real stream gives it time to).</summary>
    private static async Task ShownAsync(ChatRequest request, ChatProgress step)
    {
        request.Progress!.Report(step);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    [AvaloniaFact]
    public async Task SuggestTopicsShowsItsStepsAndItsResultAndLocksTheProfileMeanwhile()
    {
        static async Task<ChatResponse> Steps(ChatRequest request, string answer)
        {
            await ShownAsync(request, new ChatProgress(ChatPhase.Sent, TimeSpan.Zero, 0, string.Empty));
            await ShownAsync(request, new ChatProgress(ChatPhase.Thinking, TimeSpan.FromSeconds(3), 0, string.Empty));
            await ShownAsync(request, new ChatProgress(ChatPhase.Writing, TimeSpan.FromSeconds(4), 300, "{\"topics\": [\"The"));
            await ShownAsync(request, new ChatProgress(ChatPhase.Done, TimeSpan.FromSeconds(5), 320, answer));
            return new ChatResponse(answer, new ChatUsage(600, 0, 0, 120), null, "end_turn", "r");
        }

        var fake = new FakeChat()
            .AnswerAsync(r => Steps(r, "{\"topics\": [\"The fall of Trebizond\", \"Greek fire\"]}"))
            .AnswerAsync(r => Steps(r, "{\"topics\": []}"))
            .Answer(_ => throw new ProviderException("cut") { Code = "truncated", ChargedUsage = new ChatUsage(600, 0, 0, 16_000), StopReason = "max_tokens" });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        var text = h.Get<Localizer>();
        TopicsModel(page);
        var haiku = page.Settings.Picker.Row(PipelineSteps.Topics)!.Selected!.Model!.Name;
        var section = page.Topics;
        section.HasTopics.ShouldBeFalse("nothing to show before the first press");

        var shown = new List<string>();
        var working = new List<bool>();
        var strip = new List<string>();
        section.SuggestCall.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CallStatusViewModel.Text) && section.SuggestCall.Text.Length > 0)
            {
                shown.Add(section.SuggestCall.Text);
                working.Add(page.Working);
            }
        };
        var work = h.Get<TMConcept.Desktop.Work.WorkStatus>();
        work.Changed += (_, _) => strip.Add(work.Text);

        await section.SuggestAsync();

        shown.ShouldBe([text["call.preparing"], text.Format("call.sent", haiku, 0), text.Format("call.thinking", haiku, 0),
            text.Format("call.writing", haiku, 300.ToString("N0", text.Culture)), text.Format("call.done", 5)]);
        working.ShouldAllBe(w => w, "the profile cannot be switched while topics are asked for");
        strip.ShouldContain(text.Format("work.topics", text.Format("call.thinking", haiku, 0)));
        work.Active.ShouldBeFalse("the strip's step is cleared at the end");
        page.Working.ShouldBeFalse();
        section.HasTopics.ShouldBeTrue();
        section.SuggestFailed.ShouldBeFalse();

        // None came: the line says so and why, in red; the list stays empty (nothing was ticked).
        await section.SuggestAsync();
        section.HasTopics.ShouldBeFalse();
        section.SuggestFailed.ShouldBeTrue();
        section.SuggestStatus.ShouldStartWith(text.Format("topics.none", string.Empty).Split('·')[0].Trim());

        // A failure is red, says what was paid, and the activity line says it too.
        await section.SuggestAsync();
        section.SuggestFailed.ShouldBeTrue();
        section.SuggestStatus.ShouldContain(text.Format("cost.exact", string.Empty).Trim());
        var last = h.Get<TMConcept.Core.Activity.IActivityFeed>().Recent()[^1];
        last.IsError.ShouldBeTrue();
        last.Text.ShouldBe(section.SuggestStatus);
    }

    [AvaloniaFact]
    public async Task AConceptRunShowsTheCallsStepsAndHowManyConceptsAreReadySoFar()
    {
        var titles = FakeChat.Distinct(3);
        var answer = FakeChat.Json(titles);
        var fake = new FakeChat().AnswerAsync(async request =>
        {
            var twoDone = answer[..(answer.IndexOf(titles[2], StringComparison.Ordinal) - 12)];
            await ShownAsync(request, new ChatProgress(ChatPhase.Sent, TimeSpan.Zero, 0, string.Empty));
            await ShownAsync(request, new ChatProgress(ChatPhase.Thinking, TimeSpan.FromSeconds(1), 0, string.Empty));
            await ShownAsync(request, new ChatProgress(ChatPhase.Writing, TimeSpan.FromSeconds(9), 2100, twoDone));
            await ShownAsync(request, new ChatProgress(ChatPhase.Done, TimeSpan.FromSeconds(12), 3000, answer));
            return new ChatResponse(answer, new ChatUsage(1000, 0, 0, 3000), null, "end_turn", "r");
        });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        var text = h.Get<Localizer>();
        var sonnet = page.Settings.Picker.Model(PipelineSteps.Concepts)!.Name;
        page.Ideas.Count = 3;
        var progress = new List<string>();
        page.Ideas.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IdeaSectionViewModel.Progress) && page.Ideas.Progress.Length > 0)
            {
                progress.Add(page.Ideas.Progress);
            }
        };
        var work = h.Get<TMConcept.Desktop.Work.WorkStatus>();
        var strip = new List<string>();
        work.Changed += (_, _) => strip.Add(work.Text);

        await page.Ideas.GenerateAsync();

        progress.ShouldContain(text.Format("call.thinking", sonnet, 0), "not \"batch 0 / 1\": what the call does now");
        progress.ShouldContain(text.Format("call.writing", sonnet, 2100.ToString("N0", text.Culture)) + " · " + text.Format("concepts.ready", 2, 3));
        progress.ShouldContain(text.Format("call.done", 12) + " · " + text["concepts.saving"]);
        progress.ShouldNotContain(p => p.Contains(text.Format("concepts.progress", 1, 1), StringComparison.Ordinal), "one batch: no batch counter");
        strip.ShouldContain(text.Format("work.concepts", text.Format("call.thinking", sonnet, 0)));
        work.Active.ShouldBeFalse();
        page.Ideas.Progress.ShouldBeEmpty();
        page.NewConcepts.Count.ShouldBe(3);
    }

    [AvaloniaFact]
    public async Task TheBatchBudgetIsTheModelsLimitAtMost64000AndALongScriptGetsSmallerBatches()
    {
        var fake = new FakeChat();
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var profile = await PrepareAsync(h);
        profile.ContentLanguage = ContentLanguages.Turkish;
        profile.TargetSeconds = ProfileLimits.SecondsMax;
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        var page = await PageAsync(h);
        page.Ideas.Count = 20;
        var model = h.Get<TMConcept.Core.Models.IModelCatalog>().Find("anthropic", "claude-sonnet-5-5")!;
        var size = await h.Get<ConceptGenerator>().BatchSizeAsync(profile, model, string.Empty, CancellationToken.None);
        size.ShouldBeLessThan(20, "20 long Turkish scripts at the automatic (high) effort do not fit 64 000 tokens");
        var batches = ConceptGenerator.Batches(20, size).ToList();
        foreach (var batch in batches)
        {
            fake.Concepts(FakeChat.Distinct(batch));
        }

        await page.Ideas.GenerateAsync();

        // Only what is produced is paid for (the call streams): the roof is the model's limit, capped at 64 000, for every batch.
        fake.Requests.Count.ShouldBe(batches.Count);
        fake.Requests.ShouldAllBe(r => r.MaxOutputTokens == ConceptOutputBudget.Ceiling);
        ConceptOutputBudget.MaxOutputTokens(new TMConcept.Core.Models.CatalogModel { MaxOutputTokens = 8_192 }).ShouldBe(8_192);
        ConceptOutputBudget.MaxOutputTokens(new TMConcept.Core.Models.CatalogModel()).ShouldBe(ConceptOutputBudget.Ceiling, "unknown limit: 64 000");
    }

    [AvaloniaFact]
    public async Task AMeasuredAverageIsNotMultipliedByTheThinkingShareAgain()
    {
        using var h = new DesktopHarness();
        var profile = await PrepareAsync(h);
        var generator = h.Get<ConceptGenerator>();
        var model = h.Get<TMConcept.Core.Models.IModelCatalog>().Find("anthropic", "claude-sonnet-5-5")!;
        static IdeaBrief Of(int count) => new(string.Empty, count);

        var first = await generator.EstimateAsync(profile, Of(5), string.Empty, model, "high", CancellationToken.None);
        var guess = ConceptOutputBudget.TokensPerConcept(profile.ContentLanguage, profile.TargetSeconds) * 5;
        var share = ConceptOutputBudget.ThinkingShare(model, "high");
        first.OutputTokensMax.ShouldBeInRange((long)(guess * share.High) - 1, (long)(guess * share.High) + 1);
        share.High.ShouldBeGreaterThanOrEqualTo(6, "Sonnet 5.5 at effort high spent 4–6× the visible text (8 Nov 2026)");
        var low = await generator.EstimateAsync(profile, Of(5), string.Empty, model, "low", CancellationToken.None);
        low.MaxUsd.ShouldBeLessThan(first.MaxUsd, "a lower effort thinks less");
        var many = await generator.EstimateAsync(profile, Of(20), string.Empty, model, "max", CancellationToken.None);
        var size = await generator.BatchSizeAsync(profile, model, "max", CancellationToken.None);
        (size * guess / 5.0 * ConceptOutputBudget.ThinkingShare(model, "max").High).ShouldBeLessThanOrEqualTo(ConceptOutputBudget.Ceiling,
            "a batch is sized so its high end fits its max_tokens");
        many.OutputTokensMax.ShouldBeLessThanOrEqualTo(ConceptOutputBudget.Ceiling * (long)ConceptGenerator.Batches(20, size).Count());

        await h.Get<IRunStore>().AddAsync(new Run
        {
            ProfileId = profile.Id,
            Kind = RunKind.Concepts,
            Status = RunStatus.Succeeded,
            Accepted = 20,
            OutputTokens = 20 * 2_000,
            StartedAtUtc = DateTimeOffset.UtcNow,
        }, CancellationToken.None);
        var measured = await generator.EstimateAsync(profile, Of(20), string.Empty, model, string.Empty, CancellationToken.None);
        measured.OutputTokensMin.ShouldBeInRange((long)(20 * 2_000 * ConceptGenerator.MeasuredBand.Low) - 1, (long)(20 * 2_000 * ConceptGenerator.MeasuredBand.Low) + 1);
        measured.OutputTokensMax.ShouldBeInRange((long)(20 * 2_000 * ConceptGenerator.MeasuredBand.High) - 1, (long)(20 * 2_000 * ConceptGenerator.MeasuredBand.High) + 1);
    }

    [AvaloniaFact]
    public async Task PermanentDeleteSkipsHiddenCardsAndBacksUpFirstWhenItIsLarge()
    {
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s => s.AddSingleton<IConfirmDialog>(confirm));
        var profile = await PrepareAsync(h);
        var trashed = Enumerable.Range(1, 25).Select(i => new Concept
        {
            ProfileId = profile.Id,
            Title = (i == 1 ? "Hidden topic " : "Visible topic ") + Guid.NewGuid().ToString("N"),
            Summary = "s",
            Content = "c",
            Language = "en",
            Status = ConceptStatus.Trashed,
        }).ToList();
        await h.Get<IConceptStore>().AddAsync(trashed, CancellationToken.None);
        var trash = h.Get<TrashViewModel>();
        await trash.ActivateAsync();
        foreach (var card in trash.Items)
        {
            card.IsChecked = true;
        }

        trash.Search = "Visible";
        trash.Items.Count.ShouldBe(24);

        await trash.DeletePermanentlyAsync();

        (await h.Get<IConceptStore>().GetAsync(trashed[0].Id, CancellationToken.None)).ShouldNotBeNull("a card hidden by the search is not deleted");
        (await h.Get<IConceptStore>().GetAsync(trashed[1].Id, CancellationToken.None)).ShouldBeNull();
        Directory.EnumerateFiles(h.Paths.BackupDirectory, TMConcept.Storage.Backup.BackupRules.ChangePrefix + "*").ShouldNotBeEmpty();
    }

    [AvaloniaFact]
    public async Task ApproveTrashRestoreAndDeletePermanently()
    {
        var fake = new FakeChat().Concepts(["Walls of Theodosius", "Greek fire", "Iconoclasm explained"]);
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IChatClientFactory>(fake);
            s.AddSingleton<IConfirmDialog>(confirm);
        });
        await PrepareAsync(h);
        var page = await PageAsync(h);
        page.Ideas.Count = 3;
        await page.Ideas.GenerateAsync();
        var trash = h.Get<TrashViewModel>();
        await trash.ActivateAsync();

        var walls = page.NewConcepts.First(c => c.Title.Contains("Walls", StringComparison.Ordinal));
        await page.ChooseAsync(walls);
        walls.Lines.ShouldContain(l => l.IsScene);
        await page.ApproveRowAsync(walls);
        page.Approved.Count.ShouldBe(1);
        var greek = page.NewConcepts.First(c => c.Title.Contains("Greek fire", StringComparison.Ordinal));
        var iconoclasm = page.NewConcepts.First(c => c.Title.Contains("Iconoclasm", StringComparison.Ordinal));
        await page.TrashRowAsync(greek);
        await page.TrashRowAsync(iconoclasm);
        page.NewConcepts.ShouldBeEmpty();
        await Until(() => trash.Items.Count == 2);
        trash.Badge.ShouldBe("2");

        trash.Items.First(c => c.Id == iconoclasm.Id).IsChecked = true;
        await trash.RestoreAsync();
        await Until(() => page.NewConcepts.Count == 1);
        page.NewConcepts.Single().Id.ShouldBe(iconoclasm.Id);

        trash.Items.Single().IsChecked = true;
        await trash.DeletePermanentlyAsync();
        confirm.Asked.Count.ShouldBe(1);
        (await h.Get<IConceptStore>().GetAsync(greek.Id, CancellationToken.None)).ShouldBeNull();
        trash.Badge.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task ApprovedListUnapproveMovesTheConceptBackToNew()
    {
        using var h = new DesktopHarness();
        var profile = await PrepareAsync(h);
        var approved = new Concept { ProfileId = profile.Id, Title = "Walls", Summary = "s", Content = "c", Language = "en", Status = ConceptStatus.Approved };
        await h.Get<IConceptStore>().AddAsync([approved], CancellationToken.None);
        var page = await PageAsync(h);
        page.ShowApprovedList();
        page.ShowApproved.ShouldBeTrue();
        var card = page.Approved.Single();
        await page.ChooseAsync(card);
        page.Detail.CanUnapprove.ShouldBeTrue();
        page.Detail.CanApprove.ShouldBeFalse();

        await page.Detail.UnapproveAsync();

        page.Approved.ShouldBeEmpty();
        page.NewConcepts.ShouldContain(card);
        (await h.Get<IConceptStore>().GetAsync(approved.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.New);
        page.Detail.Card.ShouldBe(card, "the panel stays on it");
        page.Detail.CanApprove.ShouldBeTrue("now with the actions of a new concept");
        page.ApprovedHeader.ShouldBe(h.Get<Localizer>().Format("concepts.list.approved", 0));
    }

    [AvaloniaFact]
    public async Task RowDeleteTrashesAtOnceAndSelectsTheNext()
    {
        using var h = new DesktopHarness();
        var profile = await PrepareAsync(h);
        var concepts = Enumerable.Range(1, 3).Select(i => new Concept { ProfileId = profile.Id, Title = "Concept " + i, Summary = "s", Content = "c", Language = "en" })
            .ToList();
        await h.Get<IConceptStore>().AddAsync(concepts, CancellationToken.None);
        var page = await PageAsync(h);
        var first = page.NewConcepts.Single(c => c.Id == concepts[0].Id);
        var second = page.NewConcepts.Single(c => c.Id == concepts[1].Id);
        await page.ChooseAsync(first);

        await page.TrashRowAsync(first);
        await Until(() => ReferenceEquals(page.Detail.Card, second));

        var trashed = (await h.Get<IConceptStore>().GetAsync(concepts[0].Id, CancellationToken.None))!;
        trashed.Status.ShouldBe(ConceptStatus.Trashed);
        trashed.TrashReason.ShouldBe(TrashReason.None, "the row's Delete has no reason");
        page.NewConcepts.ShouldNotContain(first);
        page.SelectedNew.ShouldBe(second, "the list's selection moved to the next concept");
    }

    [AvaloniaFact]
    public async Task ARowActionOnAnotherConceptLeavesThePanelWhereItIs()
    {
        using var h = new DesktopHarness();
        var profile = await PrepareAsync(h);
        var concepts = Enumerable.Range(1, 3).Select(i => new Concept { ProfileId = profile.Id, Title = "Concept " + i, Summary = "s", Content = "c", Language = "en" })
            .ToList();
        await h.Get<IConceptStore>().AddAsync(concepts, CancellationToken.None);
        var page = await PageAsync(h);
        var first = page.NewConcepts.Single(c => c.Id == concepts[0].Id);
        var third = page.NewConcepts.Single(c => c.Id == concepts[2].Id);
        await page.ChooseAsync(first);

        await page.TrashRowAsync(third);
        await page.ApproveRowAsync(page.NewConcepts.Single(c => c.Id == concepts[1].Id));
        Dispatcher.UIThread.RunJobs();

        page.Detail.Card.ShouldBe(first, "the panel stays on the concept being read");
        page.SelectedNew.ShouldBe(first);
        page.NewConcepts.ShouldBe([first]);
    }

    [AvaloniaFact]
    public async Task ARowActionOnTheOpenConceptWithUnsavedEditsAsksFirst()
    {
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s => s.AddSingleton<IConfirmDialog>(confirm));
        var profile = await PrepareAsync(h);
        const string content = "[on screen: walls]\nThe walls held.";
        var walls = new Concept { ProfileId = profile.Id, Title = "Walls", Summary = "s", Content = content, Language = "en" };
        await h.Get<IConceptStore>().AddAsync([walls], CancellationToken.None);
        var page = await PageAsync(h);
        var card = page.NewConcepts.Single();
        await page.ChooseAsync(card);
        await page.Detail.EditAsync();
        page.Detail.EditText = content + "\nMy own line.";

        // "Stay": nothing changes, the edit is kept.
        confirm.Answers.Enqueue(false);
        await page.ApproveRowAsync(card);
        confirm.Messages.Single().ShouldBe(h.Get<Localizer>()["detail.unsaved"]);
        (await h.Get<IConceptStore>().GetAsync(walls.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.New);
        page.Detail.IsEditing.ShouldBeTrue();
        page.Detail.EditText.ShouldEndWith("My own line.");

        // "Discard": the row's action goes on.
        confirm.Answers.Enqueue(true);
        await page.ApproveRowAsync(card);
        (await h.Get<IConceptStore>().GetAsync(walls.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Approved);
        page.Approved.ShouldContain(card);
    }

    [AvaloniaFact]
    public async Task BothListsKeepTheStoresOrderNewestFirstWhileBatchesArriveAndAfterwards()
    {
        var fake = new FakeChat().Concepts(["Greek fire"]).Concepts(["The Nika riots", "Iconoclasm explained"]);
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        page.Ideas.Count = 1;
        await page.Ideas.GenerateAsync();
        var greek = page.NewConcepts.Single();

        page.Ideas.Count = 2;
        await page.Ideas.GenerateAsync();
        Dispatcher.UIThread.RunJobs();

        var order = page.NewConcepts.Select(c => c.Concept.Number).ToList();
        order.Count.ShouldBe(3);
        order.ShouldBe(order.OrderByDescending(n => n).ToList(), "the newest first, the same as after a reload");
        page.NewConcepts[^1].ShouldBe(greek);

        await page.ApproveRowAsync(page.NewConcepts[0]);
        await page.ApproveRowAsync(page.NewConcepts[0]);
        page.Approved.Select(c => c.Concept.Number).ShouldBe([order[0], order[1]]);
        await page.UnapproveRowAsync(page.Approved.Single(c => c.Concept.Number == order[0]));
        page.NewConcepts.Select(c => c.Concept.Number).ShouldBe([order[0], order[2]], "taken back, it goes to its place by number, not to the end");
    }

    [AvaloniaFact]
    public async Task ClickingACardOpensItInTheDetailPanelAndTheListStaysVisible()
    {
        using var h = new DesktopHarness();
        var profile = await PrepareAsync(h);
        const string content = "[on screen: walls]\nThe walls held for a thousand years.\n[on screen: ships]\nThe fleet came.";
        await h.Get<IConceptStore>().AddAsync([new Concept { ProfileId = profile.Id, Title = "Walls", Summary = "Why they held.", Content = content, Language = "en" }],
            CancellationToken.None);
        var page = await PageAsync(h);
        var (window, view) = Show(h, page);
        var text = h.Get<Localizer>();
        Named<TextBlock>(view, "EmptyLine").IsEffectivelyVisible.ShouldBeTrue("nothing chosen yet");

        page.SelectedNew = page.NewConcepts.Single();
        await Until(() => page.Detail.Lines.Count == 4);
        Dispatcher.UIThread.RunJobs();

        page.Detail.IsViewing.ShouldBeTrue();
        page.Detail.Title.ShouldBe("#1 Walls");
        page.Detail.StatusText.ShouldBe(text["detail.status.new"]);
        page.Detail.MetaLine.ShouldStartWith(text.Format("detail.meta", 10, "0:04", string.Empty).Split('·')[0].Trim(), Case.Sensitive,
            "10 narration words; the scene lines are not counted");
        Named<ListBox>(view, "NewList").IsEffectivelyVisible.ShouldBeTrue("the panel opens beside the list, not in place of it");
        Named<ItemsControl>(view, "TextLines").IsEffectivelyVisible.ShouldBeTrue();
        Named<Button>(view, "ApproveButton").IsEffectivelyVisible.ShouldBeTrue();
        Named<Button>(view, "UnapproveButton").IsEffectivelyVisible.ShouldBeFalse();
        page.NewConcepts.Single().Expanded.ShouldBeFalse("a row never opens under itself");
        window.Close();
    }

    [AvaloniaFact]
    public async Task ChangingSelectionWithUnsavedEditsAsks()
    {
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s => s.AddSingleton<IConfirmDialog>(confirm));
        var profile = await PrepareAsync(h);
        const string content = "[on screen: walls]\nThe walls held.";
        await h.Get<IConceptStore>().AddAsync([
            new Concept { ProfileId = profile.Id, Title = "Walls", Summary = "s", Content = content, Language = "en" },
            new Concept { ProfileId = profile.Id, Title = "Ships", Summary = "s", Content = content, Language = "en" },
        ], CancellationToken.None);
        var page = await PageAsync(h);
        var walls = page.NewConcepts.Single(c => c.Title.EndsWith("Walls", StringComparison.Ordinal));
        var ships = page.NewConcepts.Single(c => c.Title.EndsWith("Ships", StringComparison.Ordinal));
        await page.ChooseAsync(walls);
        await page.Detail.EditAsync();
        page.Detail.EditText = content + "\nMy own line.";

        // "Stay": the panel keeps the unsaved text and the list goes back to it.
        confirm.Answers.Enqueue(false);
        (await page.ChooseAsync(ships)).ShouldBeFalse();
        confirm.Messages.Single().ShouldBe(h.Get<Localizer>()["detail.unsaved"]);
        page.Detail.Card.ShouldBe(walls);
        page.Detail.IsEditing.ShouldBeTrue();
        page.Detail.EditText.ShouldEndWith("My own line.");
        page.SelectedNew.ShouldBe(walls);
        (await page.CanLeaveAsync()).ShouldBeTrue("the default answer lets it go");

        // Another tab or profile asks the same way.
        await page.Detail.EditAsync();
        page.Detail.EditText = content + "\nAgain.";
        confirm.Answers.Enqueue(false);
        (await page.CanLeaveAsync()).ShouldBeFalse();

        // "Discard": the other concept opens and nothing was saved.
        confirm.Answers.Enqueue(true);
        (await page.ChooseAsync(ships)).ShouldBeTrue();
        page.Detail.Card.ShouldBe(ships);
        page.Detail.IsViewing.ShouldBeTrue();
        (await h.Get<IConceptStore>().GetAsync(walls.Id, CancellationToken.None))!.Content.ShouldBe(content);
    }

    [AvaloniaFact]
    public async Task AConceptsTextIsEditedWithPlaceholdersAtTheCursorAndNoPickerIsLeftForTheModel()
    {
        using var h = new DesktopHarness();
        var profile = await PrepareAsync(h);
        var text = h.Get<Localizer>();
        const string content = "[on screen: walls]\nThe walls held.\n[on screen: ships]\nThe fleet came.";
        var approved = new Concept { ProfileId = profile.Id, Title = "Walls", Summary = "s", Content = content, Language = "en", Status = ConceptStatus.Approved };
        var fresh = new Concept { ProfileId = profile.Id, Title = "Fleet", Summary = "s", Content = content, Language = "en" };
        await h.Get<IConceptStore>().AddAsync([approved, fresh], CancellationToken.None);
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();
        var (window, view) = Show(h, page);
        view.GetVisualDescendants().OfType<TMConcept.Desktop.Views.MarkerPicker>().Where(p => p.IsEffectivelyVisible)
            .ShouldBeEmpty("the idea is plain text: no placeholder goes to the concept model");

        // An approved concept opens in the panel's editor; a placeholder goes in at the cursor.
        page.ShowApprovedList();
        await page.ChooseAsync(page.Approved.Single());
        await page.Detail.EditAsync();
        page.Detail.IsEditing.ShouldBeTrue();
        page.Detail.EditText.ShouldBe(content);
        Dispatcher.UIThread.RunJobs();
        var box = Named<TextBox>(view, "EditBox");
        box.IsEffectivelyVisible.ShouldBeTrue();
        Named<ListBox>(view, "ApprovedList").IsEffectivelyVisible.ShouldBeTrue("the list stays in sight while editing");
        var picker = view.GetVisualDescendants().OfType<TMConcept.Desktop.Views.MarkerPicker>().Single();
        picker.Markers!.Select(m => m.Token).ShouldContain("[soft transition]");
        box.CaretIndex = content.IndexOf("The fleet", StringComparison.Ordinal);
        picker.Insert(picker.Markers!.Single(m => m.Token == "[soft transition]"));
        Dispatcher.UIThread.RunJobs();
        await page.Detail.SaveEditAsync();

        page.Detail.IsViewing.ShouldBeTrue();
        var saved = (await h.Get<IConceptStore>().GetAsync(approved.Id, CancellationToken.None))!;
        saved.Content.ShouldBe("[on screen: walls]\nThe walls held.\n[on screen: ships]\n[soft transition] The fleet came.");
        saved.Status.ShouldBe(ConceptStatus.Approved);
        TMConcept.Core.Scenes.SceneSplitter.Split(saved.Content).Scenes[1].Transition.ShouldBe(TMConcept.Core.Scenes.SceneTransitions.Fade);

        // A text with nothing to narrate is not saved; Cancel leaves the stored text as it was.
        page.ShowNewList();
        await page.ChooseAsync(page.NewConcepts.Single());
        await page.Detail.EditAsync();
        page.Detail.EditText = "[on screen: only a picture]";
        await page.Detail.SaveEditAsync();
        page.Detail.EditStatus.ShouldBe(text["concepts.edit.invalid"]);
        page.Detail.IsEditing.ShouldBeTrue();
        page.Detail.CancelEdit();
        page.Detail.IsViewing.ShouldBeTrue();
        (await h.Get<IConceptStore>().GetAsync(fresh.Id, CancellationToken.None))!.Content.ShouldBe(content);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheMeaningCheckEstimateCountsTheEarlierConceptsItEmbedsFirst()
    {
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(new FakeChat()));
        var profile = await PrepareAsync(h);
        await h.Get<IConceptStore>().AddAsync(FakeChat.Distinct(30).Select(t => new Concept { ProfileId = profile.Id, Title = t, Summary = "s", Language = "en" }).ToList(),
            CancellationToken.None);
        var generator = h.Get<ConceptGenerator>();
        var catalog = h.Get<TMConcept.Core.Models.IModelCatalog>();
        var online = catalog.Find("openai", "text-embedding-3-small")!;
        var local = catalog.Find(TMConcept.Core.Models.ModelProviders.Local, "paraphrase-multilingual-minilm")!;

        var paid = await generator.EstimateEmbeddingAsync(profile, online, 10, CancellationToken.None);
        var free = await generator.EstimateEmbeddingAsync(profile, local, 10, CancellationToken.None);

        // 10 new + 30 earlier concepts without a vector, 80 tokens each (English).
        paid.Backfill.ShouldBe(30);
        paid.Estimate.InputTokens.ShouldBe(40 * ConceptGenerator.EmbeddingTokensPerConcept);
        paid.Estimate.MaxUsd.ShouldBe(40 * ConceptGenerator.EmbeddingTokensPerConcept * online.InputUsd / 1_000_000m);
        free.Backfill.ShouldBe(30);
        free.Estimate.MaxUsd.ShouldBe(0);

        // The estimate's tooltip says so.
        var page = await PageAsync(h);
        var row = page.Settings.Picker.Row(PipelineSteps.Embedding)!;
        row.Selected = row.Options.First(o => o.Model?.Id == local.Id);
        await Until(() => page.Ideas.EstimateDetail.Contains(h.Get<Localizer>().Format("concepts.estimate.backfill", 30), StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task ARestoredProfileWhoseLocalMeaningModelIsNotInstalledAsksToInstallItInsteadOfFailing()
    {
        // After a restore the profile still chooses the local MiniLM meaning check, but the tools folder is not in a backup.
        var embedding = new FakeComponent(TMConcept.Core.Components.ComponentCodes.EmbeddingPrefix + "paraphrase-multilingual-minilm", ready: false);
        var confirm = new FakeConfirm();
        var fake = new FakeChat().Concepts(FakeChat.Distinct(2));
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IChatClientFactory>(fake);
            s.AddSingleton<IConfirmDialog>(confirm);
            Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<TMConcept.Core.Components.IComponentSource>(s);
            s.AddSingleton<TMConcept.Core.Components.IComponentSource>(new FakeComponents(embedding));
        });
        var profile = await PrepareAsync(h);
        await h.Get<TMConcept.Core.Models.IStepModelStore>().SaveAsync(new TMConcept.Core.Models.StepModelChoice
        {
            Step = PipelineSteps.Embedding, ProfileId = profile.Id, Provider = "local", Model = "paraphrase-multilingual-minilm",
        }, CancellationToken.None);
        var page = await PageAsync(h);
        page.Settings.Picker.Row(PipelineSteps.Embedding)!.Selected!.Model!.Id.ShouldBe("paraphrase-multilingual-minilm", "the restored choice is loaded");
        page.Ideas.Count = 2;

        // Declined: nothing starts, nothing is paid, and the section says why (no provider error).
        confirm.Answers.Enqueue(false);
        await page.Ideas.GenerateAsync();

        confirm.Messages.ShouldHaveSingleItem().ShouldContain(embedding.Name);
        embedding.Installs.ShouldBe(0);
        fake.Requests.ShouldBeEmpty();
        page.Settings.Picker.Status.ShouldNotBeEmpty();
        page.Settings.Picker.Status.ShouldNotContain("is not installed");
        page.Ideas.Status.ShouldBe(page.Settings.Picker.Status);
        page.Ideas.StatusIsError.ShouldBeTrue();
    }
}
