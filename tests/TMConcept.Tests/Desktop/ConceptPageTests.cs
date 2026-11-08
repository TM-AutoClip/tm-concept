using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Keys;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;
using TMConcept.Core.Runs;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

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

    private static async Task<ConceptsViewModel> PageAsync(DesktopHarness h)
    {
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();
        var row = page.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Concepts)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        return page;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 150 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        condition().ShouldBeTrue();
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
        h.Get<TMConcept.Desktop.Localization.Localizer>().Use("tr");
        var page = await PageAsync(h);
        await page.ProfileChangedAsync();
        var row = page.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Concepts)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");

        // Nothing asks the user to know the subject (period, region, field…), whatever the profile's template; every choice starts on "any".
        h.Get<IFocusSource>().Dimensions.Select(d => d.Id).ShouldBe(["tone", "audience", "structure"]);
        page.FocusRows.Select(r => r.Dimension.Id).ShouldBe(["tone", "audience", "structure"]);
        page.FocusRows.ShouldAllBe(r => r.Chosen == null);
        page.FocusRows[0].Title.ShouldBe("Ton", "the drop-downs speak the interface language");
        page.FocusRows[0].Options[0].Label.ShouldBe("fark etmez");
        page.FineTuneHeader.ShouldBe("İnce ayar (isteğe bağlı)");
        page.FocusRows[0].Selected = page.FocusRows[0].Options.First(o => o.Value == "mysterious");
        page.FineTuneHeader.ShouldBe("İnce ayar (isteğe bağlı) · gizemli", "the folded title tells what is chosen");
        page.Count = 3;

        await page.GenerateAsync();

        var user = fake.Requests.Single().User;
        user.ShouldContain("FOCUS (the tone, audience and structure the user chose; follow it): Tone: mysterious", Case.Sensitive,
            "the profile writes English videos: the focus line is English");
        user.ShouldNotContain("Period");
        user.ShouldNotContain("CATEGORY");
    }

    [AvaloniaFact]
    public async Task TheLeftPanelFoldsFineTuningAndModelsAndPutsBothChecksOnOneLine()
    {
        using var h = new DesktopHarness();
        await PrepareAsync(h);
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();

        // A new profile has no concept model: the folded "Models" opens by itself and says so.
        page.ModelsOpen.ShouldBeTrue();
        page.ModelsHeader.ShouldBe(text["concepts.models.none"]);

        // Compact cards for the concept and topics models; the meaning check is not a card, it stands next to the word check.
        page.Picker.Compact.ShouldBeTrue();
        page.Picker.Cards.Select(c => c.Step.Code).ShouldBe([TMConcept.Core.Pipeline.PipelineSteps.Concepts, TMConcept.Core.Pipeline.PipelineSteps.Topics]);
        page.Picker.Rows.Select(r => r.Step.Code).ShouldContain(TMConcept.Core.Pipeline.PipelineSteps.Embedding, "it is still prepared and saved");
        page.Meaning.ShouldBeSameAs(page.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Embedding));

        var concepts = page.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Concepts)!;
        concepts.ShowRecommended.ShouldBeTrue("nothing recommended is chosen yet");
        concepts.Selected = concepts.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        page.ModelsHeader.ShouldBe(text.Format("concepts.models.header", concepts.Selected!.Model!.Name), "the folded title names the concept model");
        concepts.ShowRecommended.ShouldBeFalse("a recommended model is chosen: the chip would only repeat it");

        var view = new TMConcept.Desktop.Views.ConceptsView { DataContext = page };
        var window = new Avalonia.Controls.Window { Content = view, Width = 1280, Height = 820 };
        h.Get<TMConcept.Desktop.Localization.LanguageResources>().Attach(window.Resources.MergedDictionaries);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var all = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).ToList();
        all.OfType<Avalonia.Controls.Expander>().Single(e => e.Name == "FineTune").IsExpanded.ShouldBeFalse("fine-tuning is optional and folded");
        all.OfType<Avalonia.Controls.Expander>().Single(e => e.Name == "Models").IsExpanded.ShouldBeTrue();
        all.OfType<TMConcept.Desktop.Views.HelpTip>().Select(t => t.Text).ShouldContain(text["concepts.check.hint"]);

        // The long explanations are "?" tips, not lines that are always open.
        var shown = all.OfType<Avalonia.Controls.TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        foreach (var key in new[] { "concepts.check.hint", "focus.hint", "concepts.keys", "step.embedding.purpose", "step.concepts.purpose", "topics.hint" })
        {
            shown.ShouldNotContain(text[key], key);
        }

        window.Close();
    }

    [AvaloniaFact]
    public async Task SuggestTopicsBringsTopicsThatGoIntoTheExtraRequestAndCountsTheCost()
    {
        var fake = new FakeChat().Answer(_ => new ChatResponse("{\"topics\": [\"The fall of Trebizond\", \"Greek fire\", \"greek  fire\", \"\"]}",
            new ChatUsage(600, 0, 0, 120), null, "end_turn", "r"));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var profile = await PrepareAsync(h);
        await h.Get<IConceptStore>().AddAsync([new Concept { ProfileId = profile.Id, Title = "Hagia Sophia", Summary = "s", Content = "c", Language = "en" }],
            CancellationToken.None);
        var page = await PageAsync(h);
        var topicsRow = page.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Topics)!;
        page.TopicEstimate.ShouldNotBeEmpty();
        await page.SuggestTopicsAsync();
        page.TopicStatus.ShouldBe(h.Get<TMConcept.Desktop.Localization.Localizer>()["topics.no_model"], "nothing is asked without a model");
        fake.Requests.ShouldBeEmpty();

        topicsRow.Selected = topicsRow.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        await Until(() => page.TopicEstimate.Contains("Claude Haiku", StringComparison.Ordinal));
        page.FocusRows.First(r => r.Dimension.Id == "tone").Selected = page.FocusRows.First(r => r.Dimension.Id == "tone").Options[1];
        page.ExtraPrompt = "only sieges";

        await page.SuggestTopicsAsync();

        page.Topics.ShouldBe(["The fall of Trebizond", "Greek fire"], "cleaned, no repeats, no empty ones");
        var request = fake.Requests.Single();
        request.User.ShouldContain("- Hagia Sophia", Case.Sensitive, "earlier titles are not suggested again");
        request.User.ShouldContain("EXTRA REQUEST SO FAR: «only sieges»");
        request.User.ShouldContain("FOCUS: ");
        request.MaxOutputTokens.ShouldBe(TopicSuggester.OutputMax, "room for thinking (1 500 cut Haiku at effort high before the first topic)");
        request.Stream.ShouldBeTrue();
        request.Effort.ShouldBe(TopicSuggester.Effort, "a cheap, quick call: always effort low");
        topicsRow.HasEfforts.ShouldBeFalse("the topics step shows no effort picker");
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Single(r => r.Kind == RunKind.Topics);
        run.ActualUsd.ShouldBeGreaterThan(0);
        page.TopicStatus.ShouldContain(h.Get<TMConcept.Desktop.Localization.Localizer>().Usd(run.ActualUsd));

        page.AddTopic("Greek fire");
        page.ExtraPrompt.ShouldBe("only sieges\nGreek fire");
        page.Topics.ShouldBe(["The fall of Trebizond"]);
    }

    [Fact]
    public void TheWordCheckAndTheMeaningCheckAreTwoDifferentChecksAndSayIt()
    {
        using var h = new DesktopHarness();
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        foreach (var language in new[] { "tr", "en" })
        {
            text.Use(language);
            text["concepts.check.local"].ShouldNotBe(text["concepts.check.meaning"]);
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
        var page = await PageAsync(h);
        page.Count = 25;
        page.Estimate.ShouldContain("Claude Sonnet 5.5");

        await page.GenerateAsync();

        fake.Requests.Count.ShouldBe(2);
        fake.KeysUsed.ShouldAllBe(k => k == Secret);
        fake.Requests[0].CacheSystem.ShouldBeTrue();
        fake.Requests[1].User.ShouldContain("- " + first[19]);
        fake.Requests[1].System.ShouldBe(fake.Requests[0].System, "the cached part must not change between batches");
        page.NewConcepts.Count.ShouldBe(24);
        page.Missing.ShouldBe(1);
        page.LastCost.ShouldNotBeEmpty();
        var numbers = (await h.Get<IConceptStore>().ListAsync(profile.Id, [ConceptStatus.New], false, CancellationToken.None)).Select(c => c.Number).ToList();
        numbers.ShouldBe(Enumerable.Range(1, 24).ToList(), ignoreOrder: true);
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Single();
        run.Status.ShouldBe(RunStatus.Succeeded);
        run.Accepted.ShouldBe(24);
        run.RejectedDuplicates.ShouldBe(1);
        run.ActualUsd.ShouldBe(2 * ((1000 * 2.00m) + (2000 * 10.00m)) / 1_000_000m);
    }

    [AvaloniaFact]
    public async Task AnErrorKeepsTheFinishedBatchesAndTheBilledCost()
    {
        var fake = new FakeChat()
            .Concepts(FakeChat.Distinct(20))
            .Answer(_ => throw new ProviderException("cut") { Code = "truncated", ChargedUsage = new ChatUsage(1000, 0, 0, 1000) });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        page.Count = 40;

        await page.GenerateAsync();

        page.NewConcepts.Count.ShouldBe(20);
        page.Missing.ShouldBe(20);
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
        page.Count = 20;

        await page.GenerateAsync();

        page.NewConcepts.Count.ShouldBe(3);
        page.Missing.ShouldBe(17);
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Single();
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldBe("truncated");
        run.Accepted.ShouldBe(3);
        run.OutputTokens.ShouldBe(36_000);
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        page.Status.ShouldBe(text.Format("concepts.cut", 3, 17, text.Usd(run.ActualUsd)), "it says how many were saved and how many are missing");
        fake.Requests.Count.ShouldBe(1, "no extra batch is started without asking");
    }

    [AvaloniaFact]
    public async Task FillTheMissingAsksWithTheEstimateAndAsksForExactlyTheMissingCount()
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
        page.Count = 5;
        page.ExtraPrompt = "five concepts: one on Hagia Sophia, one on religions";

        await page.GenerateAsync();
        page.NewConcepts.Count.ShouldBe(2);
        page.Missing.ShouldBe(3);
        fake.Requests[0].MaxOutputTokens.ShouldBe(ConceptOutputBudget.Ceiling, "the model's limit (128 000) capped at 64 000, not a size from the count");

        await page.FillMissingAsync();
        confirm.Asked.Count.ShouldBe(1);
        confirm.Messages.Single().ShouldContain("$", Case.Sensitive, "the question shows the estimate");
        fake.Requests.Count.ShouldBe(1, "declined: nothing is asked or paid for");

        confirm.Answer = true;
        await page.FillMissingAsync();
        fake.Requests.Count.ShouldBe(2);
        fake.Requests[1].User.ShouldContain("REQUESTED: 3 new concepts. Write exactly 3 concepts, even when the extra request names another number.");
        page.NewConcepts.Count.ShouldBe(5);
        page.Missing.ShouldBe(0);
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
        var topicsRow = page.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Topics)!;
        topicsRow.Selected = topicsRow.Options.First(o => o.Model?.Id == "claude-haiku-5-5");

        await page.SuggestTopicsAsync();

        page.Topics.ShouldBe(["The fall of Trebizond", "Greek \"fire\""], "the unfinished last topic is dropped");
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Single(r => r.Kind == RunKind.Topics);
        run.Status.ShouldBe(RunStatus.Succeeded);
        run.OutputTokens.ShouldBe(16_000);
        page.TopicStatus.ShouldBe(text.Format("topics.done_cut", 2, text.Usd(run.ActualUsd)));
        page.TopicStatus.ShouldNotContain(text["concepts.cut.title"]);
    }

    [AvaloniaFact]
    public async Task ATopicRequestThatFailsWithoutAProviderErrorStillSaysWhy()
    {
        var fake = new FakeChat().Answer(_ => throw new InvalidOperationException("broken pipe"));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        var topicsRow = page.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Topics)!;
        topicsRow.Selected = topicsRow.Options.First(o => o.Model?.Id == "claude-haiku-5-5");

        await page.SuggestTopicsAsync();

        page.TopicStatus.ShouldContain("broken pipe");
        page.Suggesting.ShouldBeFalse();
    }

    /// <summary>Reports a step of a fake call and waits until the interface thread has handled it (as a real stream gives it time to).</summary>
    private static async Task ShownAsync(ChatRequest request, ChatProgress step)
    {
        request.Progress!.Report(step);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    [AvaloniaFact]
    public async Task SuggestTopicsShowsItsStepsAndItsResultInACardAndLocksTheProfileMeanwhile()
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
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        var topicsRow = page.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Topics)!;
        topicsRow.Selected = topicsRow.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        var haiku = topicsRow.Selected!.Model!.Name;
        page.TopicCardShown.ShouldBeFalse("nothing to show before the first press");

        var shown = new List<string>();
        var working = new List<bool>();
        var strip = new List<string>();
        page.TopicCall.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CallStatusViewModel.Text) && page.TopicCall.Text.Length > 0)
            {
                shown.Add(page.TopicCall.Text);
                working.Add(page.Working);
            }
        };
        var work = h.Get<TMConcept.Desktop.Work.WorkStatus>();
        work.Changed += (_, _) => strip.Add(work.Text);

        await page.SuggestTopicsAsync();

        shown.ShouldBe([text["call.preparing"], text.Format("call.sent", haiku, 0), text.Format("call.thinking", haiku, 0),
            text.Format("call.writing", haiku, 300.ToString("N0", text.Culture)), text.Format("call.done", 5)]);
        working.ShouldAllBe(w => w, "the profile cannot be switched while topics are asked for");
        strip.ShouldContain(text.Format("work.topics", text.Format("call.thinking", haiku, 0)));
        work.Active.ShouldBeFalse("the strip's step is cleared at the end");
        page.Working.ShouldBeFalse();
        page.TopicCardShown.ShouldBeTrue();
        page.HasTopics.ShouldBeTrue();
        page.TopicsHeader.ShouldBe(text.Format("topics.title", 2));
        page.TopicFailed.ShouldBeFalse();

        // None came: the card says so and why, in red.
        await page.SuggestTopicsAsync();
        page.HasTopics.ShouldBeFalse();
        page.TopicCardShown.ShouldBeTrue();
        page.TopicFailed.ShouldBeTrue();
        page.TopicStatus.ShouldStartWith(text.Format("topics.none", string.Empty).Split('·')[0].Trim());

        // A failure is red, says what was paid, and the activity line says it too.
        await page.SuggestTopicsAsync();
        page.TopicFailed.ShouldBeTrue();
        page.TopicStatus.ShouldContain(text.Format("cost.exact", string.Empty).Trim());
        var last = h.Get<TMConcept.Core.Activity.IActivityFeed>().Recent()[^1];
        last.IsError.ShouldBeTrue();
        last.Text.ShouldBe(page.TopicStatus);
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
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        var sonnet = page.Picker.Model(TMConcept.Core.Pipeline.PipelineSteps.Concepts)!.Name;
        page.Count = 3;
        var progress = new List<string>();
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConceptsViewModel.Progress) && page.Progress.Length > 0)
            {
                progress.Add(page.Progress);
            }
        };
        var work = h.Get<TMConcept.Desktop.Work.WorkStatus>();
        var strip = new List<string>();
        work.Changed += (_, _) => strip.Add(work.Text);

        await page.GenerateAsync();

        progress.ShouldContain(text.Format("call.thinking", sonnet, 0), "not \"batch 0 / 1\": what the call does now");
        progress.ShouldContain(text.Format("call.writing", sonnet, 2100.ToString("N0", text.Culture)) + " · " + text.Format("concepts.ready", 2, 3));
        progress.ShouldContain(text.Format("call.done", 12) + " · " + text["concepts.saving"]);
        progress.ShouldNotContain(p => p.Contains(text.Format("concepts.progress", 1, 1), StringComparison.Ordinal), "one batch: no batch counter");
        strip.ShouldContain(text.Format("work.concepts", text.Format("call.thinking", sonnet, 0)));
        work.Active.ShouldBeFalse();
        page.Progress.ShouldBeEmpty();
        page.NewConcepts.Count.ShouldBe(3);
    }

    [AvaloniaFact]
    public async Task TheBatchBudgetIsTheModelsLimitAtMost64000WhateverTheLength()
    {
        var fake = new FakeChat().Concepts(FakeChat.Distinct(20));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var profile = await PrepareAsync(h);
        profile.ContentLanguage = ContentLanguages.Turkish;
        profile.TargetSeconds = ProfileLimits.SecondsMax;
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        var page = await PageAsync(h);
        page.Count = 20;

        await page.GenerateAsync();

        // Only what is produced is paid for (the call streams): the roof is the model's limit, capped at 64 000.
        fake.Requests.Single().MaxOutputTokens.ShouldBe(ConceptOutputBudget.Ceiling);
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

        var first = await generator.EstimateAsync(profile, 5, string.Empty, string.Empty, model, "high", CancellationToken.None);
        var guess = ConceptOutputBudget.TokensPerConcept(profile.ContentLanguage, profile.TargetSeconds) * 5;
        var share = ConceptOutputBudget.ThinkingShare(model, "high");
        first.OutputTokensMax.ShouldBeInRange((long)(guess * share.High) - 1, (long)(guess * share.High) + 1);
        share.High.ShouldBeGreaterThanOrEqualTo(6, "Sonnet 5.5 at effort high spent 4–6× the visible text (8 Nov 2026)");
        var low = await generator.EstimateAsync(profile, 5, string.Empty, string.Empty, model, "low", CancellationToken.None);
        low.MaxUsd.ShouldBeLessThan(first.MaxUsd, "a lower effort thinks less");
        var many = await generator.EstimateAsync(profile, 20, string.Empty, string.Empty, model, "max", CancellationToken.None);
        many.OutputTokensMax.ShouldBeLessThanOrEqualTo(ConceptOutputBudget.Ceiling, "a batch never produces more than its max_tokens");

        await h.Get<IRunStore>().AddAsync(new Run
        {
            ProfileId = profile.Id,
            Kind = RunKind.Concepts,
            Status = RunStatus.Succeeded,
            Accepted = 20,
            OutputTokens = 20 * 2_000,
            StartedAtUtc = DateTimeOffset.UtcNow,
        }, CancellationToken.None);
        var measured = await generator.EstimateAsync(profile, 20, string.Empty, string.Empty, model, string.Empty, CancellationToken.None);
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
        page.Count = 3;
        await page.GenerateAsync();
        var trash = h.Get<TrashViewModel>();
        await trash.ActivateAsync();

        var walls = page.NewConcepts.First(c => c.Title.Contains("Walls", StringComparison.Ordinal));
        page.Selected = walls;
        await Until(() => walls.Lines.Count > 0);
        walls.Lines.ShouldContain(l => l.IsScene);
        await page.ApproveAsync(walls);
        page.Approved.Count.ShouldBe(1);
        var greek = page.NewConcepts.First(c => c.Title.Contains("Greek fire", StringComparison.Ordinal));
        var iconoclasm = page.NewConcepts.First(c => c.Title.Contains("Iconoclasm", StringComparison.Ordinal));
        await page.TrashAsync(greek);
        await page.TrashAsync(iconoclasm);
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
    public async Task AConceptsTextIsEditedWithPlaceholdersAtTheCursorAndNoPickerIsLeftForTheModel()
    {
        using var h = new DesktopHarness();
        var profile = await PrepareAsync(h);
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        const string content = "[on screen: walls]\nThe walls held.\n[on screen: ships]\nThe fleet came.";
        var approved = new Concept { ProfileId = profile.Id, Title = "Walls", Summary = "s", Content = content, Language = "en", Status = ConceptStatus.Approved };
        var fresh = new Concept { ProfileId = profile.Id, Title = "Fleet", Summary = "s", Content = content, Language = "en" };
        await h.Get<IConceptStore>().AddAsync([approved, fresh], CancellationToken.None);
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();

        var view = new TMConcept.Desktop.Views.ConceptsView { DataContext = page };
        var window = new Avalonia.Controls.Window { Content = view, Width = 1280, Height = 820 };
        h.Get<TMConcept.Desktop.Localization.LanguageResources>().Attach(window.Resources.MergedDictionaries);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<TMConcept.Desktop.Views.MarkerPicker>().Where(p => p.IsEffectivelyVisible)
            .ShouldBeEmpty("the extra request is plain text: no placeholder goes to the concept model");

        // An approved concept opens in the editor in place of the list; a placeholder goes in at the cursor.
        await page.EditAsync(page.Approved.Single());
        page.Editing.ShouldBeTrue();
        page.EditText.ShouldBe(content);
        Dispatcher.UIThread.RunJobs();
        var all = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).ToList();
        var box = all.OfType<Avalonia.Controls.TextBox>().Single(b => b.Name == "EditBox");
        box.IsEffectivelyVisible.ShouldBeTrue();
        all.OfType<Avalonia.Controls.ListBox>().Single(l => l.Name == "NewList").IsEffectivelyVisible.ShouldBeFalse();
        var picker = all.OfType<TMConcept.Desktop.Views.MarkerPicker>().Single();
        picker.Markers!.Select(m => m.Token).ShouldContain("[soft transition]");
        box.CaretIndex = content.IndexOf("The fleet", StringComparison.Ordinal);
        picker.Insert(picker.Markers!.Single(m => m.Token == "[soft transition]"));
        Dispatcher.UIThread.RunJobs();
        await page.SaveEditAsync();

        page.Editing.ShouldBeFalse();
        var saved = (await h.Get<IConceptStore>().GetAsync(approved.Id, CancellationToken.None))!;
        saved.Content.ShouldBe("[on screen: walls]\nThe walls held.\n[on screen: ships]\n[soft transition] The fleet came.");
        saved.Status.ShouldBe(ConceptStatus.Approved);
        TMConcept.Core.Scenes.SceneSplitter.Split(saved.Content).Scenes[1].Transition.ShouldBe(TMConcept.Core.Scenes.SceneTransitions.Fade);

        // A text with nothing to narrate is not saved; Cancel leaves the stored text as it was.
        var card = page.NewConcepts.Single();
        await page.EditAsync(card);
        page.EditText = "[on screen: only a picture]";
        await page.SaveEditAsync();
        page.EditStatus.ShouldBe(text["concepts.edit.invalid"]);
        page.Editing.ShouldBeTrue();
        page.CancelEdit();
        page.Editing.ShouldBeFalse();
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

        // The page says so next to the estimate.
        var page = await PageAsync(h);
        var row = page.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Embedding)!;
        row.Selected = row.Options.First(o => o.Model?.Id == local.Id);
        await Until(() => page.Estimate.Contains(h.Get<TMConcept.Desktop.Localization.Localizer>().Format("concepts.estimate.backfill", 30), StringComparison.Ordinal));
    }
}
