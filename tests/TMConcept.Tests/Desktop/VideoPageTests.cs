using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Components;
using TMConcept.Core.Engines;
using TMConcept.Core.Images;
using TMConcept.Core.Keys;
using TMConcept.Core.Media;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Tools;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

public sealed class VideoPageTests
{
    private const string Content = "[on screen: a map]\nIn 672 the city was under siege.\n[on screen: burning ships]\nA strange fire burned on the water.";

    private static ChatResponse Code(string code) => new("```js\n" + code + "\n```", new ChatUsage(500, 0, 0, 800), null, "end_turn", "r");

    private static async Task<(VideoViewModel Page, Concept Concept)> PrepareAsync(DesktopHarness h, Action<VideoViewModel>? options = null)
    {
        var (page, concept) = await OpenAsync(h);
        options?.Invoke(page);
        await page.EnqueueAsync(page.Approved.Single());
        return (page, concept);
    }

    /// <summary>A profile with a key, its video code choice and one approved concept; the page is open (nothing queued).</summary>
    private static async Task<(VideoViewModel Page, Concept Concept)> OpenAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "k", Secret = "fake-test-key-video-0123" }, CancellationToken.None);
        var concept = new Concept { ProfileId = profile.Id, Title = "Greek fire", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Approved };
        await h.Get<IConceptStore>().AddAsync([concept], CancellationToken.None);
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice
        {
            Step = PipelineSteps.VideoCode,
            ProfileId = profile.Id,
            Provider = "anthropic",
            Model = "claude-sonnet-5-5",
        }, CancellationToken.None);
        var page = h.Get<VideoViewModel>();
        await page.ActivateAsync();
        return (page, concept);
    }

    private static DesktopHarness Harness(FakeChat chat, FakeEngine engine, Action<IServiceCollection>? more = null) => new(s =>
    {
        s.AddSingleton<IChatClientFactory>(chat);
        s.RemoveAll<IVideoEngine>();
        s.AddSingleton<IVideoEngine>(engine);
        s.AddSingleton<IMediaTools, FakeMedia>();
        more?.Invoke(s);
    });

    [AvaloniaFact]
    public async Task AQueuedConceptBecomesASilentVideoInTheLibrary()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine);
        var (page, concept) = await PrepareAsync(h);
        page.Queue.Count.ShouldBe(1);
        page.Approved.ShouldBeEmpty();

        await page.ProduceAllAsync();

        page.Queue.ShouldBeEmpty();
        page.Produced.Single().Title.ShouldBe("Greek fire");
        var video = (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single();
        video.Warnings.ShouldContain(VideoWarnings.SpeechOff);
        video.TotalUsd.ShouldBeGreaterThan(0);
        var folder = LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, video.Folder);
        Path.GetFileName(folder).ShouldBe(VideoFolders.FirstVersion, "a video is a version folder inside its concept's folder");
        File.Exists(Path.Combine(folder, "video.mp4")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(folder, VideoFolders.Source, engine.CodeFile)).Trim().ShouldBe(engine.SampleCode, "the model's code as it rendered");
        File.Exists(Path.Combine(folder, VideoFolders.Source, "input.json")).ShouldBeTrue();
        Directory.Exists(Path.Combine(folder, VideoFolders.Source, "tmp")).ShouldBeFalse("the engine's temporary files are not kept");
        Directory.Exists(Path.Combine(folder, VideoFolders.Audio)).ShouldBeFalse("a silent video has no clips");
        File.ReadAllText(Path.Combine(folder, "subtitles.srt")).ShouldContain("siege");
        File.ReadAllText(Path.Combine(folder, "narration.txt")).ShouldContain("Scene 2");
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Produced);
        engine.Input!.Scenes.Count.ShouldBe(2);

        // No activity panel: the notification says the video is ready, what it cost and that it is silent.
        var toast = h.Get<TMConcept.Desktop.Notifications.ToastNotifier>().Open.Last().DataContext as TMConcept.Desktop.Notifications.ToastViewModel;
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        toast!.IsWarning.ShouldBeTrue();
        toast.Message.ShouldContain(text.Format("video.silent.off", "Greek fire"));
        toast.Message.ShouldContain(text.Usd(video.TotalUsd));
    }

    [AvaloniaFact]
    public async Task ModelsAreOneFoldedSummaryLineThatOpensToThePickers()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat(), engine);
        var (page, _) = await OpenAsync(h);
        var (window, view) = Show(h, page, 1280, 720);
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();

        // One line: code, engine, images, narration and the concept model of the AI edit; the cards are folded away.
        page.ModelsOpen.ShouldBeFalse();
        page.Picker.Rows.Select(r => r.Step.Code).ShouldBe([PipelineSteps.VideoCode, PipelineSteps.Images, PipelineSteps.Speech, PipelineSteps.Concepts]);
        var line = Find<Avalonia.Controls.TextBlock>(view, "SummaryLine").Text!;
        foreach (var part in new[] { "video-code", "engine", "images", "speech", "concepts" })
        {
            line.ShouldContain(text["steps.short." + part] + ":");
        }

        Find<TMConcept.Desktop.Views.StepPickerView>(view, "ModelPickers").IsEffectivelyVisible.ShouldBeFalse();
        Find<Avalonia.Controls.Primitives.ToggleButton>(view, "Header").IsChecked = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        page.ModelsOpen.ShouldBeTrue();
        Find<TMConcept.Desktop.Views.StepPickerView>(view, "ModelPickers").IsEffectivelyVisible.ShouldBeTrue();
        window.Close();
    }

    [AvaloniaFact]
    public async Task PageAt1280x720HasApprovedDetailQueueAndProducedSideBySide()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat(), engine);
        var (page, _) = await OpenAsync(h);
        var (window, view) = Show(h, page, 1280, 720);

        Avalonia.Rect At(string name)
        {
            var control = Find<Avalonia.Controls.Control>(view, name);
            control.IsEffectivelyVisible.ShouldBeTrue(name);
            var origin = Avalonia.VisualExtensions.TranslatePoint(control, new Avalonia.Point(0, 0), view)!.Value;
            return new Avalonia.Rect(origin, control.Bounds.Size);
        }

        var approved = At("ApprovedList");
        var detail = At("Detail");
        var queue = At("QueueList");
        var produced = At("ProducedList");
        detail.Top.ShouldBeGreaterThanOrEqualTo(approved.Bottom, "the detail panel is under the approved list");
        queue.Left.ShouldBeGreaterThan(Math.Max(approved.Right, detail.Right));
        produced.Left.ShouldBeGreaterThan(queue.Right, "the made videos stand right of the queue");
        foreach (var area in new[] { approved, detail, queue, produced })
        {
            area.Right.ShouldBeLessThanOrEqualTo(1280);
            area.Bottom.ShouldBeLessThanOrEqualTo(720);
        }

        foreach (var area in new[] { approved, detail, queue })
        {
            area.Height.ShouldBeGreaterThan(60);
        }

        // The made videos' list takes only the height of its rows: the preview under it gets the rest.
        produced.Height.ShouldBeGreaterThan(30);
        produced.Height.ShouldBeLessThan(At("ProducedSplit").Height * 0.4 + 1);
        queue.Width.ShouldBeLessThan(At("ProducedSplit").Width, "the made videos get the larger share");

        Avalonia.VisualTree.VisualExtensions.GetVisualChildren(view).OfType<Avalonia.Controls.ScrollViewer>().ShouldBeEmpty("the page does not scroll as a whole");

        // A narrow window: the made videos go under the queue.
        window.Width = 1100;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        At("ProducedList").Top.ShouldBeGreaterThan(At("QueueList").Bottom);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ClickingAnApprovedConceptOpensTheDetailAndDoesNotEnqueue()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat(), engine);
        var (page, concept) = await OpenAsync(h);

        page.SelectedApproved = page.Approved.Single();
        await Until(() => page.Detail.Card is not null);

        page.Detail.Card!.Id.ShouldBe(concept.Id);
        page.Detail.IsViewing.ShouldBeTrue();
        page.Detail.CanUnapprove.ShouldBeTrue();
        page.Detail.CanApprove.ShouldBeFalse("nothing is approved or added here");
        page.Detail.CanEdit.ShouldBeTrue();
        page.Queue.ShouldBeEmpty("a click opens the concept, it does not queue it");
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Approved);
    }

    [AvaloniaFact]
    public async Task EnqueueButtonEnqueues()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat(), engine);
        var (page, concept) = await OpenAsync(h);
        await page.ChooseAsync(page.Approved.Single());

        await page.EnqueueAsync(page.Approved.Single());

        page.Queue.Single().Item.ConceptId.ShouldBe(concept.Id);
        page.Approved.ShouldBeEmpty();
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Queued);
        page.Detail.ReadOnlyNote.ShouldBe(h.Get<TMConcept.Desktop.Localization.Localizer>()["detail.queued"], "a queued concept is shown read-only");
        page.Detail.CanEdit.ShouldBeFalse();
        page.QueueHeader.ShouldContain("1");

        // The item keeps only its subtitles: the models are read when it starts.
        var stored = (await h.Get<IQueueStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single();
        var options = System.Text.Json.JsonSerializer.Deserialize(stored.SettingsJson, TMConcept.Core.Render.CoreJsonContext.Default.VideoOptions)!;
        options.Subtitles.ShouldBeTrue();
        options.CodeModel.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task AiEditFromTheVideoPageUsesTheConceptStep()
    {
        var engine = new FakeEngine();
        const string edited = "[on screen: the harbour chain]\nThe chain held the fleet out.\n[on screen: ships on rollers]\nSo the ships went over the hill.";
        var chat = new FakeChat().Answer(_ => new ChatResponse("{\"konseptler\":[{\"baslik\":\"Greek fire\",\"ozet\":\"s\",\"icerik\":"
            + System.Text.Json.JsonSerializer.Serialize(edited) + "}]}", new ChatUsage(900, 0, 0, 700), null, "end_turn", "r"));
        using var h = Harness(chat, engine);
        var (page, concept) = await OpenAsync(h);
        var row = page.Picker.Row(PipelineSteps.Concepts)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        await page.ChooseAsync(page.Approved.Single());

        await page.Detail.AiEditAsync();
        page.Detail.AiInstruction = "Tell how the ships were carried over the hill.";
        await page.Detail.SendAiEditAsync();

        chat.Models.Single().Id.ShouldBe("claude-haiku-5-5", "the concept step chosen on this page");
        page.Detail.IsEditing.ShouldBeTrue("the result waits in the editor");
        await page.Detail.SaveEditAsync();
        var saved = (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!;
        saved.Content.ShouldContain("over the hill");
        saved.Status.ShouldBe(ConceptStatus.Approved);
        page.Queue.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task DeleteFromTheVideoPageTrashesTheConcept()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat(), engine);
        var (page, concept) = await OpenAsync(h);
        await page.ChooseAsync(page.Approved.Single());

        await page.Detail.TrashJustAsync();
        await Until(() => page.Approved.Count == 0);

        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Trashed);
        page.Detail.IsEmpty.ShouldBeTrue();

        // Taking one back from approved also leaves this page (it is not approved here again).
        var second = new Concept { ProfileId = concept.ProfileId, Title = "Iconoclasm", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Approved };
        await h.Get<IConceptStore>().AddAsync([second], CancellationToken.None);
        await page.ActivateAsync();
        await page.ChooseAsync(page.Approved.Single());
        await page.Detail.UnapproveAsync();
        await Until(() => page.Approved.Count == 0);
        page.Detail.IsEmpty.ShouldBeTrue();
        (await h.Get<IConceptStore>().GetAsync(second.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.New);
    }

    [AvaloniaFact]
    public async Task ProductionReportsStagesSpentAndCodeTokens()
    {
        var engine = new FakeEngine();
        var images = new FakeImages();
        var chat = new FakeChat();
        using var h = Harness(chat, engine, s =>
        {
            s.RemoveAll<IImageEngine>();
            s.AddSingleton<IImageEngine>(images);
        });
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        var (page, concept) = await OpenAsync(h);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "fal", Label = "k", Secret = "fake-test-key-video-4567" }, CancellationToken.None);
        await page.ActivateAsync();
        var imageRow = page.Picker.Row(PipelineSteps.Images)!;
        imageRow.Selected = imageRow.Options.First(o => o.Model?.Id == "fal-ai/flux-2/flash");
        await page.EnqueueAsync(page.Approved.Single());
        var row = page.Queue.Single();

        // While the code model writes: the stages so far, as the screen shows them.
        (StageState State, string Count, string Cost, string Detail)[] during = [];
        decimal spentDuring = 0;
        chat.Answer(_ => new ChatResponse("{\"prompts\":[\"a map\",\"burning ships\"]}", new ChatUsage(100, 0, 0, 50), null, "end_turn", "r"))
            .AnswerAsync(async request =>
            {
                request.Progress.ShouldNotBeNull("the code call reports its phase and tokens");
                request.Progress!.Report(new ChatProgress(ChatPhase.Writing, TimeSpan.FromSeconds(3), 4321, "window"));
                await Task.Delay(50);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    during = row.Progress!.Stages.Select(s => (s.State, s.CountText, s.CostText, s.Detail)).ToArray();
                    spentDuring = row.Progress.SpentUsd;
                });
                return Code(engine.SampleCode);
            });

        await page.ProduceAllAsync();

        var steps = TMConcept.Core.Videos.VideoSteps.All.ToList();
        (StageState State, string Count, string Cost, string Detail) At(string step) => during[steps.IndexOf(step)];
        At(VideoSteps.Prepare).State.ShouldBe(StageState.Done);
        At(VideoSteps.Speech).State.ShouldBe(StageState.Skipped, "narration is off");
        At(VideoSteps.Speech).Count.ShouldBe(text["video.stage.skipped"]);
        At(VideoSteps.Images).State.ShouldBe(StageState.Done);
        At(VideoSteps.Images).Count.ShouldBe(text.Format("video.stage.count", 2, 2));
        At(VideoSteps.Images).Cost.ShouldNotBeEmpty("the image prompts and the two images were paid");
        At(VideoSteps.Code).State.ShouldBe(StageState.Running);
        At(VideoSteps.Code).Detail.ShouldContain(4321.ToString("N0", text.Culture));
        At(VideoSteps.Render).State.ShouldBe(StageState.Waiting);
        spentDuring.ShouldBeGreaterThan(0.02m - 0.0000001m);

        var video = (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single();
        row.Progress!.SpentUsd.ShouldBe(video.TotalUsd, "the stages show the same money as the record");
        row.Progress.SpentUsd.ShouldBeGreaterThan(spentDuring, "the code call was paid after it");
        row.Progress.Stages.Where(s => s.Code != VideoSteps.Speech).ShouldAllBe(s => s.State == StageState.Done);
        row.Progress.Stages.Single(s => s.Code == VideoSteps.Code).CostText.ShouldNotBeEmpty();
        page.LastLine.ShouldContain(text.Usd(video.TotalUsd));
    }

    [AvaloniaFact]
    public async Task AFailedVideoShowsWhatItSpent()
    {
        var engine = new FakeEngine { FailRenders = 2 };
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode)).Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine);
        var (page, _) = await PrepareAsync(h);
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        var row = page.Queue.Single();

        await page.ProduceAllAsync();

        var model = h.Get<IModelCatalog>().Find("anthropic", "claude-sonnet-5-5")!;
        var paid = 2 * h.Get<TMConcept.Core.Costs.ICostCalculator>().Exact(model, new ChatUsage(500, 0, 0, 800));
        row.Failed.ShouldBeTrue();
        row.Error.ShouldContain(text.Usd(paid));
        page.LastLine.ShouldBe(text.Format("video.last.failed", row.Title, text.Usd(paid)));
        row.Progress!.SpentUsd.ShouldBe(paid);
        row.Progress.Stages.Single(s => s.Code == VideoSteps.Render).State.ShouldBe(StageState.Failed);
        row.Progress.Stages.Single(s => s.Code == VideoSteps.Code).State.ShouldBe(StageState.Done);
    }

    [AvaloniaFact]
    public async Task ClickingAProducedVideoLoadsThePreviewAndTheButtons()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine);
        var (page, _) = await PrepareAsync(h);
        await page.ProduceAllAsync();
        var (window, view) = Show(h, page, 1280, 720);
        Find<Avalonia.Controls.Button>(view, "PlaySystemButton").IsEffectivelyVisible.ShouldBeFalse("no video is chosen yet");

        var made = page.Produced.Single();
        page.SelectedProduced = made;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        page.Player.Poster.ShouldBe(made.Thumbnail);
        page.Player.DurationMs.ShouldBe(made.Video.DurationMs);
        page.Player.IsPlaying.ShouldBeFalse("nothing starts by itself");
        foreach (var name in new[] { "Preview", "PlaySystemButton", "OpenFolderButton", "ProduceAgainButton" })
        {
            Find<Avalonia.Controls.Control>(view, name).IsEffectivelyVisible.ShouldBeTrue(name);
        }

        // A file deleted from outside: "Open in player" says so instead of opening anything.
        File.Delete(made.File);
        page.PlaySystem();
        page.Status.ShouldBe(h.Get<TMConcept.Desktop.Localization.Localizer>()["produced.file_missing"]);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ProduceAgainBringsTheConceptBackSelectedForEditing()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine);
        var (page, concept) = await PrepareAsync(h);
        await page.ProduceAllAsync();
        page.Approved.ShouldBeEmpty();
        page.SelectedProduced = page.Produced.Single();

        await page.ProduceAgainAsync();

        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Approved);
        page.Approved.Single().Id.ShouldBe(concept.Id);
        page.SelectedApproved.ShouldBe(page.Approved.Single());
        page.Detail.Card!.Id.ShouldBe(concept.Id);
        page.Detail.IsViewing.ShouldBeTrue();
        page.Detail.CanEdit.ShouldBeTrue("one click to Edit or Edit with AI");

        // Again while it is approved: refused with the existing message.
        await page.ProduceAgainAsync();
        page.Status.ShouldBe(h.Get<TMConcept.Desktop.Localization.Localizer>()["produced.again.already"]);
    }

    [AvaloniaFact]
    public async Task LeavingThePageClosesThePlayer()
    {
        using var h = new DesktopHarness(PlayerTests.PlaceholderFfmpeg);
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        var folder = Path.Combine(h.Get<IAppPaths>().LibraryDirectory, "Byzantium", "2026-11-11-1-walls", VideoFolders.FirstVersion);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "video.mp4"), new byte[16]);
        await h.Get<IVideoStore>().AddAsync(new VideoRecord { ProfileId = profile.Id, Title = "Walls", FileName = "video.mp4", DurationMs = 10_000,
            Folder = LibraryPaths.Relative(h.Get<IAppPaths>().LibraryDirectory, folder) }, [], CancellationToken.None);
        var page = h.Get<VideoViewModel>();
        await page.ActivateAsync();
        page.SelectedProduced = page.Produced.Single();
        page.Player.HasVideo.ShouldBeTrue(page.Player.Message);
        page.Player.IsPlaying = true;

        page.Deactivate();

        page.Player.IsPlaying.ShouldBeFalse("another tab was opened: the video stops");
        page.Player.Frame.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task TheMoneyShownDuringARunIsWhatTheRecordAndAFailureSay()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine);
        var (_, concept) = await OpenAsync(h);
        var reports = new System.Collections.Concurrent.ConcurrentQueue<VideoProgress>();
        var progress = new ForwardProgress<VideoProgress>(reports.Enqueue);

        var record = await RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = false }, progress);

        record.TotalUsd.ShouldBeGreaterThan(0);
        reports.Last().SpentUsd.ShouldBe(record.TotalUsd);
        reports.Select(r => r.Share).ShouldBeInOrder();
        reports.Last().Step.ShouldBeEmpty("after the last step nothing runs");
        reports.Last().Plan.ShouldBe([VideoSteps.Prepare, VideoSteps.Timing, VideoSteps.Code, VideoSteps.Render, VideoSteps.Mux, VideoSteps.Save]);

        var second = new Concept { ProfileId = concept.ProfileId, Title = "Iconoclasm", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Queued };
        await h.Get<IConceptStore>().AddAsync([second], CancellationToken.None);
        engine.FailRenders = 2;
        chat.Answer(_ => Code(engine.SampleCode)).Answer(_ => Code(engine.SampleCode));
        reports.Clear();
        var failure = await Should.ThrowAsync<VideoException>(() => RunAsync(h, second, new VideoOptions { Engine = engine.Kind, Speech = false }, progress));
        failure.SpentUsd.ShouldBeGreaterThan(0);
        reports.Last().SpentUsd.ShouldBe(failure.SpentUsd);
    }

    [AvaloniaFact]
    public async Task AVoicedVideoKeepsItsSceneClipsAndTheNarrationText()
    {
        var engine = new FakeEngine();
        var voice = new FakeVoice();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine, s =>
        {
            s.RemoveAll<TMConcept.Core.Speech.ISpeechEngine>();
            s.AddSingleton<TMConcept.Core.Speech.ISpeechEngine>(voice);
        });
        var (_, concept) = await PrepareAsync(h);
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice { Step = PipelineSteps.Speech, ProfileId = concept.ProfileId, Provider = ModelProviders.Local,
            Model = voice.Code }, CancellationToken.None);
        (await h.Get<TMConcept.Core.Pronunciation.PronunciationService>().AddAsync(ContentLanguages.English, "siege", "seej", CancellationToken.None)).ShouldBeNull();

        var record = await RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = true });

        var folder = LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, record.Folder);
        Directory.GetFiles(Path.Combine(folder, VideoFolders.Audio)).Select(Path.GetFileName).ShouldBe(["1.wav", "2.wav"], ignoreOrder: true);
        File.ReadAllText(Path.Combine(folder, VideoFolders.NarrationFile)).ShouldContain("strange fire");
        File.ReadAllText(Path.Combine(folder, VideoFolders.Source, "input.json")).ShouldContain("siege");

        // The pronunciation dictionary changes only what the voice reads; subtitles and the narration file keep the written word.
        voice.Requests.First().Text.ShouldBe("In 672 the city was under seej.");
        File.ReadAllText(Path.Combine(folder, "subtitles.srt")).ShouldContain("under siege");
        File.ReadAllText(Path.Combine(folder, VideoFolders.NarrationFile)).ShouldNotContain("seej");
        record.Warnings.ShouldNotContain(VideoWarnings.SpeechFailed);
    }

    [AvaloniaFact]
    public async Task AFailedVideoLeavesNoVersionFolderBehind()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine, s =>
        {
            s.RemoveAll<IMediaTools>();
            s.AddSingleton<IMediaTools>(new FakeMedia { FailThumbnail = true });
        });
        var (_, concept) = await PrepareAsync(h);

        (await Should.ThrowAsync<VideoException>(() => RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = false }))).Code.ShouldBe("files");

        var profileFolder = Path.Combine(h.Get<IAppPaths>().LibraryDirectory, "Byzantium");
        (Directory.Exists(profileFolder) ? Directory.GetDirectories(profileFolder) : []).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task RejectedCodeGetsOneRepair()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Code("fetch('x')")).Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine);
        var (page, concept) = await PrepareAsync(h);

        await page.ProduceAllAsync();

        chat.Requests.Count.ShouldBe(2);
        chat.Requests[1].User.ShouldContain("network request");
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single().Warnings.ShouldContain(VideoWarnings.Repaired);
    }

    [AvaloniaFact]
    public async Task ARenderThatFailsTwiceStaysInTheQueueWithItsError()
    {
        var engine = new FakeEngine { FailRenders = 2 };
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode)).Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine);
        var (page, concept) = await PrepareAsync(h);

        await page.ProduceAllAsync();

        page.Queue.Single().Failed.ShouldBeTrue();
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Queued);
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldBeEmpty();

        // Two code calls were paid (800 output tokens each): the row says what the failure cost, the queue item keeps it for a restart,
        // and the month's spending counts it as a failed video.
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        var model = h.Get<IModelCatalog>().Find("anthropic", "claude-sonnet-5-5")!;
        var paid = 2 * h.Get<TMConcept.Core.Costs.ICostCalculator>().Exact(model, new ChatUsage(500, 0, 0, 800));
        page.Queue.Single().Error.ShouldEndWith(text.Format("video.failed.spent", text.Usd(paid)));
        var item = (await h.Get<IQueueStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single();
        (item.LastErrorCode, item.FailedUsd).ShouldBe(("render", paid));
        var lost = (await h.Get<TMConcept.Core.Runs.IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None))
            .Single(r => r.Kind == TMConcept.Core.Runs.RunKind.FailedVideo);
        (lost.ActualUsd, lost.Status, lost.Error).ShouldBe((paid, TMConcept.Core.Runs.RunStatus.Failed, "render"));
        var spending = await TMConcept.Desktop.Localization.PriceInfo.SpendingAsync(h.Get<TMConcept.Core.Runs.IRunStore>(), h.Get<IProfileStore>(),
            h.Get<IVideoStore>(), TimeProvider.System, text, CancellationToken.None);
        spending.Breakdown.ShouldContain(text.Format("models.spending.failed", text.Usd(paid)));

        // A second failure adds to what the item lost.
        engine.FailRenders = 2;
        chat.Answer(_ => Code(engine.SampleCode)).Answer(_ => Code(engine.SampleCode));
        await page.ProduceAllAsync();
        (await h.Get<IQueueStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single().FailedUsd.ShouldBe(2 * paid);
        page.Queue.Single().Error.ShouldEndWith(text.Format("video.failed.spent", text.Usd(2 * paid)));
    }

    [AvaloniaFact]
    public async Task AMadeVideoCarriesItsConceptShareOutsideItsOwnTotal()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine);
        var (_, concept) = await PrepareAsync(h);

        // The concept came from a run of 0.11 $ that kept 5 concepts.
        var run = new TMConcept.Core.Runs.Run { ProfileId = concept.ProfileId, Kind = TMConcept.Core.Runs.RunKind.Concepts, ActualUsd = 0.11m, Accepted = 5,
            CostExact = true, Status = TMConcept.Core.Runs.RunStatus.Succeeded };
        await h.Get<TMConcept.Core.Runs.IRunStore>().AddAsync(run, CancellationToken.None);
        var generated = new Concept { ProfileId = concept.ProfileId, Title = "Iconoclasm", Summary = "s", Content = Content, Language = "en",
            Status = ConceptStatus.Queued, RunId = run.Id };
        await h.Get<IConceptStore>().AddAsync([generated], CancellationToken.None);
        var record = await RunAsync(h, generated, new VideoOptions { Engine = engine.Kind, Speech = false });

        var costs = await h.Get<IVideoStore>().CostsAsync(record.Id, CancellationToken.None);
        var share = costs.Single(c => c.Step == PipelineSteps.Concepts);
        (share.Usd, share.RunId, share.Exact).ShouldBe((0.022m, (Guid?)run.Id, true));
        record.TotalUsd.ShouldBe(costs.Where(c => c.Step != PipelineSteps.Concepts).Sum(c => c.Usd));
    }

    [AvaloniaFact]
    public async Task ACutOffVideoCodeSaysSoWithWhatWasPaidAndTheCallUsesTheWholeBudgetAndStreams()
    {
        var engine = new FakeEngine();
        var charged = new ChatUsage(900, 0, 4000, 64_000);
        var chat = new FakeChat().Answer(_ => throw new TMConcept.Core.Providers.ProviderException("cut")
        {
            Code = "truncated",
            ChargedUsage = charged,
            PartialText = "window.__timelines = ",
        });
        using var h = Harness(chat, engine);
        var (page, concept) = await PrepareAsync(h);
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        var model = h.Get<IModelCatalog>().Find("anthropic", "claude-sonnet-5-5")!;
        var paid = h.Get<TMConcept.Core.Costs.ICostCalculator>().Exact(model, charged);

        await page.ProduceAllAsync();

        chat.Requests.Single().MaxOutputTokens.ShouldBe(VideoCodeBudget.MaxOutputTokens);
        chat.Requests.Single().Stream.ShouldBeTrue();
        var error = page.Queue.Single().Error;
        error.ShouldBe(text.Format("video.error.truncated", text.Usd(paid)));
        error.ShouldNotContain(text["provider.fail.truncated"]);
        page.LastLine.ShouldContain(text.Usd(paid));
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Queued);
    }

    [AvaloniaFact]
    public async Task ANarrationModelThatCannotBeResolvedGivesASilentVideoNotACrash()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine);
        var (_, concept) = await PrepareAsync(h);

        // Kokoro speaks English but is not installed in the test folder: resolving the voice fails before anything is spoken.
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice { Step = PipelineSteps.Speech, ProfileId = concept.ProfileId, Provider = ModelProviders.Local,
            Model = "kokoro" }, CancellationToken.None);

        var record = await RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = true });

        record.Warnings.ShouldContain(VideoWarnings.SpeechFailed);
        record.Warnings.ShouldNotContain(VideoWarnings.SpeechOff);
        record.SpeechModel.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task SampleImagesChosenWithoutAnySampleSayWhyTheVideoHasNoImages()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine);
        var (_, concept) = await PrepareAsync(h);

        var record = await RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = false, Images = ImageSources.Samples });

        record.Warnings.ShouldContain(VideoWarnings.SamplesNone);
        engine.Input!.Scenes.ShouldAllBe(s => s.Image.Length == 0);
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        text["produced.note." + VideoWarnings.SamplesNone].ShouldNotStartWith("produced.note.");
    }

    private static async Task<VideoRecord> RunAsync(DesktopHarness h, Concept concept, VideoOptions options, IProgress<VideoProgress>? progress = null)
    {
        var profile = (await h.Get<IProfileStore>().GetAsync(concept.ProfileId, CancellationToken.None))!;
        var model = new ResolvedModel(h.Get<IModelCatalog>().Find("anthropic", "claude-sonnet-5-5")!,
            new ApiKey { Provider = "anthropic", Label = "k", Secret = "fake-test-key-video-0123" }, string.Empty);
        var stored = (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!;
        return await h.Get<VideoPipeline>().RunAsync(new VideoJob(stored, profile, options, model), progress, CancellationToken.None);
    }

    private static (Avalonia.Controls.Window Window, TMConcept.Desktop.Views.VideoView View) Show(DesktopHarness h, VideoViewModel page, int width, int height)
    {
        var view = new TMConcept.Desktop.Views.VideoView { DataContext = page };
        var window = new Avalonia.Controls.Window { Content = view, Width = width, Height = height };
        h.Get<TMConcept.Desktop.Localization.LanguageResources>().Attach(window.Resources.MergedDictionaries);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static T Find<T>(Avalonia.Visual root, string name)
        where T : Avalonia.Controls.Control =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>().First(c => c.Name == name);

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        condition().ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task AnEngineThatIsNotInstalledIsOfferedAndDecliningStopsTheItemBeforeAnythingIsPaid()
    {
        var engine = new FakeEngine { Missing = ["packages", "browser"] };
        var chat = new FakeChat();
        var confirm = new FakeConfirm { Answer = false };
        var node = new FakeComponent(ComponentCodes.Node, ready: true);
        var ffmpeg = new FakeComponent(ComponentCodes.Ffmpeg, ready: false);
        using var h = Harness(chat, engine, s =>
        {
            s.AddSingleton<TMConcept.Desktop.Dialogs.IConfirmDialog>(confirm);
            s.RemoveAll<IComponentSource>();
            s.AddSingleton<IComponentSource>(new FakeComponents(node, ffmpeg, new TMConcept.Engines.Components.EngineComponent(engine)));
        });
        var (page, concept) = await PrepareAsync(h);
        page.Picker.Row(PipelineSteps.VideoCode)!.Note.ShouldContain(h.Get<TMConcept.Desktop.Localization.Localizer>()["models.installs_on_start"]);

        await page.ProduceAllAsync();

        confirm.Messages.Single().ShouldContain("Chrome Headless Shell");
        confirm.Messages.Single().ShouldContain(ffmpeg.Name);
        ffmpeg.Installs.ShouldBe(0);
        chat.Requests.ShouldBeEmpty();
        page.Queue.Single().Error.ShouldContain(h.Get<TMConcept.Desktop.Localization.Localizer>()["video.error.missing"]);
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Queued);
    }

    [AvaloniaFact]
    public async Task ItemsWithImagesAndNarrationOffAskForNoLocalImageOrVoiceModel()
    {
        var engine = new FakeEngine();
        var confirm = new FakeConfirm { Answer = false };
        var imageRuntime = new FakeComponent(ComponentCodes.ImageRuntime, ready: false);
        var weights = new FakeComponent(ComponentCodes.ImagePrefix + "z-image-turbo", ready: false, ComponentCodes.ImageRuntime);
        var speechRuntime = new FakeComponent(ComponentCodes.SpeechRuntime, ready: false);
        var voice = new FakeComponent(ComponentCodes.VoicePrefix + "supertonic", ready: false, ComponentCodes.SpeechRuntime);
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine, s =>
        {
            s.AddSingleton<TMConcept.Desktop.Dialogs.IConfirmDialog>(confirm);
            s.RemoveAll<IComponentSource>();
            s.AddSingleton<IComponentSource>(new FakeComponents(imageRuntime, weights, speechRuntime, voice,
                new TMConcept.Engines.Components.EngineComponent(engine)));
        });
        // The profile's last choices are local models that are not installed; on the page images and narration are turned off.
        var (page, concept) = await PrepareAsync(h);
        var store = h.Get<IStepModelStore>();
        await store.SaveAsync(new StepModelChoice { Step = PipelineSteps.Images, ProfileId = concept.ProfileId, Provider = ModelProviders.Local, Model = "z-image-turbo" },
            CancellationToken.None);
        await store.SaveAsync(new StepModelChoice { Step = PipelineSteps.Speech, ProfileId = concept.ProfileId, Provider = ModelProviders.Local, Model = "supertonic" },
            CancellationToken.None);
        await page.ActivateAsync();
        page.Picker.Row(PipelineSteps.Images)!.Selected!.Model!.Id.ShouldBe("z-image-turbo", "the profile's last choice is shown");
        page.Picker.Row(PipelineSteps.Images)!.Selected = page.Picker.Row(PipelineSteps.Images)!.Options[0];
        page.Picker.Row(PipelineSteps.Speech)!.Selected = page.Picker.Row(PipelineSteps.Speech)!.Options[0];

        await page.ProduceAllAsync();

        confirm.Asked.ShouldBeEmpty();
        StepModelResolver.IsOff(await store.GetAsync(PipelineSteps.Images, concept.ProfileId, CancellationToken.None)).ShouldBeTrue("the new choice is kept");
        (weights.Installs + imageRuntime.Installs + voice.Installs + speechRuntime.Installs).ShouldBe(0);
        page.Queue.ShouldBeEmpty();
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single().ImageModel.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task AVoiceHiddenAfterTheLanguageChangedFallsBackToTheLanguagesDefaultVoice()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine);
        var (page, concept) = await PrepareAsync(h);

        // The profile spoke English with Kokoro; then its content language became Turkish, which Kokoro does not speak.
        var store = h.Get<IStepModelStore>();
        await store.SaveAsync(new StepModelChoice { Step = PipelineSteps.Speech, ProfileId = concept.ProfileId, Provider = ModelProviders.Local, Model = "kokoro" },
            CancellationToken.None);
        var profile = (await h.Get<IProfileStore>().GetAsync(concept.ProfileId, CancellationToken.None))!;
        profile.ContentLanguage = ContentLanguages.Turkish;
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        await page.ActivateAsync();

        // The page shows the Turkish default voice with a warning (0.1.6 turned narration off); the old voice is never what a video uses.
        page.Picker.Model(PipelineSteps.Speech)!.Id.ShouldBe("supertonic");
        page.Picker.Choice(PipelineSteps.Speech)!.Model.ShouldBe("supertonic");
        var row = page.Picker.Row(PipelineSteps.Speech)!;
        row.NoteIsWarning.ShouldBeTrue();
        row.Note.ShouldContain("Kokoro");
    }

    [AvaloniaFact]
    public async Task AnItemRemovedDuringTheRunIsNotMadeAndANewOneIs()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat();
        using var h = Harness(chat, engine);
        var (page, first) = await PrepareAsync(h);
        var second = new Concept { ProfileId = first.ProfileId, Title = "Iconoclasm", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Approved };
        var third = new Concept { ProfileId = first.ProfileId, Title = "Nika riots", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Approved };
        await h.Get<IConceptStore>().AddAsync([second, third], CancellationToken.None);
        await page.ActivateAsync();
        await page.EnqueueAsync(page.Approved.Single(c => c.Id == second.Id));
        var queue = h.Get<IQueueStore>();
        chat.Answer(_ =>
        {
            // While the first video is being made the user removes the second item (✕) and queues the third.
            var items = queue.ListAsync(first.ProfileId, CancellationToken.None).GetAwaiter().GetResult();
            queue.RemoveAsync(items.Single(i => i.ConceptId == second.Id).Id, CancellationToken.None).GetAwaiter().GetResult();
            h.Get<IConceptStore>().SetStatusAsync([second.Id], ConceptStatus.Approved, CancellationToken.None).GetAwaiter().GetResult();
            queue.AddAsync(new QueueItem { ProfileId = first.ProfileId, ConceptId = third.Id, Position = 9, SettingsJson = items[0].SettingsJson },
                CancellationToken.None).GetAwaiter().GetResult();
            h.Get<IConceptStore>().SetStatusAsync([third.Id], ConceptStatus.Queued, CancellationToken.None).GetAwaiter().GetResult();
            return Code(engine.SampleCode);
        }).Answer(_ => Code(engine.SampleCode));

        await page.ProduceAllAsync();

        var made = await h.Get<IVideoStore>().ListAsync(first.ProfileId, CancellationToken.None);
        made.Select(v => v.Title).ShouldBe(["Greek fire", "Nika riots"], ignoreOrder: true);
        (await h.Get<IConceptStore>().GetAsync(second.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Approved);
        chat.Requests.Count.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task TheCodeModelChosenOnThePageIsUsedAndKeptForTheProfile()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine);
        var (page, concept) = await PrepareAsync(h);
        var code = page.Picker.Row(PipelineSteps.VideoCode)!;
        code.Selected!.Model!.Id.ShouldBe("claude-sonnet-5-5", "the profile's last choice is preselected");
        code.Selected = code.Options.First(o => o.Model?.Id == "claude-opus-5-5");
        page.QueueEstimate.ShouldContain("$");

        await page.ProduceAllAsync();

        page.Queue.ShouldBeEmpty();
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single().CodeModel.ShouldBe("anthropic/claude-opus-5-5");
        (await h.Get<IStepModelStore>().GetAsync(PipelineSteps.VideoCode, concept.ProfileId, CancellationToken.None))!.Model.ShouldBe("claude-opus-5-5");
        page.LastLine.ShouldNotBeEmpty();
    }

    [AvaloniaFact]
    public async Task AHighEffortOnTheVideoCodeStepShowsAWarningButIsKept()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat(), engine);
        var (page, _) = await PrepareAsync(h);
        var code = page.Picker.Row(PipelineSteps.VideoCode)!;
        code.Selected = code.Options.First(o => o.Model?.Id == "claude-opus-5-5");
        code.HasEfforts.ShouldBeTrue();
        code.EffortWarning.ShouldBeFalse("Automatic is no warning");

        foreach (var level in new[] { "high", "xhigh", "max" }.Where(l => code.Efforts.Any(e => e.Value == l)))
        {
            code.Effort = code.Efforts.Single(e => e.Value == level);
            code.EffortWarning.ShouldBeTrue(level);
            code.Effort.Value.ShouldBe(level, "the choice is kept, not forced down");
        }

        code.Effort = code.Efforts.First(e => e.Value == "medium");
        code.EffortWarning.ShouldBeFalse();

        // Only the video code step warns (a long concept batch at high effort is the user's choice there).
        var concepts = new StepModelRowViewModel(PipelineSteps.All.Single(s => s.Code == PipelineSteps.Concepts), "c", "p", string.Empty, "auto");
        concepts.Selected = code.Selected;
        concepts.Effort = concepts.Efforts.First(e => e.Value == "high");
        concepts.EffortWarning.ShouldBeFalse();

        var view = new TMConcept.Desktop.Views.StepPickerView { DataContext = page.Picker };
        var window = new Avalonia.Controls.Window { Content = view, Width = 1280, Height = 720 };
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        h.Get<TMConcept.Desktop.Localization.LanguageResources>().Attach(window.Resources.MergedDictionaries);
        code.Effort = code.Efforts.First(e => e.Value == "high");
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.TextBlock>()
            .Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ShouldContain(text["step.effort.costly"]);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheImagesStepOffMakesNoImageCallAndAnImageModelGetsItsPromptsFromTheCodeModel()
    {
        var engine = new FakeEngine();
        var images = new FakeImages();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine, s =>
        {
            s.RemoveAll<IImageEngine>();
            s.AddSingleton<IImageEngine>(images);
        });
        var (page, concept) = await PrepareAsync(h);
        page.Picker.Row(PipelineSteps.Images)!.Selected!.Kind.ShouldBe(StepOptionKind.None, "a profile starts with images off");

        await page.ProduceAllAsync();

        images.Prompts.ShouldBeEmpty();
        chat.Requests.Count.ShouldBe(1);
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single().ImageModel.ShouldBeEmpty();

        // With an image model: the code model writes the prompts first (the same model, one call), then each scene gets its image.
        var second = new Concept { ProfileId = concept.ProfileId, Title = "Iconoclasm", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Approved };
        await h.Get<IConceptStore>().AddAsync([second], CancellationToken.None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "fal", Label = "k", Secret = "fake-test-key-video-4567" }, CancellationToken.None);
        chat.Answer(_ => new ChatResponse("{\"prompts\":[\"a map\",\"burning ships\"]}", new ChatUsage(100, 0, 0, 50), null, "end_turn", "r"))
            .Answer(_ => Code(engine.SampleCode));
        await page.ActivateAsync();
        var row = page.Picker.Row(PipelineSteps.Images)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "fal-ai/flux-2/flash");
        await page.EnqueueAsync(page.Approved.Single());

        await page.ProduceAllAsync();

        images.Prompts.ShouldBe(["a map", "burning ships"]);
        chat.Requests.Count.ShouldBe(3);
        chat.Requests[1].System.ShouldBe(ImagePromptBuilder.System);
        var made = (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single(v => v.Title == "Iconoclasm");
        made.ImageModel.ShouldBe("fal/fal-ai/flux-2/flash");
    }

    internal sealed class FakeImages : IImageEngine
    {
        public List<string> Prompts { get; } = [];

        public string Code => "fal";

        public Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken cancellationToken)
        {
            Prompts.Add(request.Prompt);
            Directory.CreateDirectory(Path.GetDirectoryName(request.Output)!);
            File.WriteAllBytes(request.Output, [137, 80, 78, 71]);
            return Task.FromResult(new ImageResult(request.Output, 0.01m));
        }
    }

    internal sealed class FakeEngine : IVideoEngine
    {
        public int FailRenders { get; set; }

        public TMConcept.Core.Render.RenderInput? Input { get; private set; }

        public EngineKind Kind { get; init; } = EngineKind.HyperFrames;

        public string Name { get; init; } = "Fake";

        public int Renders { get; private set; }

        public bool Experimental => false;

        public bool NeedsLicenseConsent => false;

        public string CodeFile => "scene.js";

        public string Contract => "contract";

        public string SampleCode => "window.__timelines = window.__timelines || {}; window.__timelines[\"concept\"] = gsap.timeline({ paused: true });";

        public IReadOnlyList<string> Missing { get; set; } = [];

        public EngineStatus Status() => new(Missing.Count == 0, Missing);

        public Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

        public void WriteProject(EngineRenderRequest request)
        {
            var findings = EngineCodeCheck.Check(Kind, request.Code);
            if (findings.Count > 0)
            {
                throw new EngineException("rejected") { Code = "check", Findings = findings };
            }

            Input = request.Input;
            Directory.CreateDirectory(Path.Combine(request.Folder, "tmp"));
            File.WriteAllText(Path.Combine(request.Folder, CodeFile), request.Code);
            File.WriteAllText(Path.Combine(request.Folder, "input.json"), TMConcept.Core.Render.RenderInputBuilder.ToJson(request.Input));
            File.WriteAllText(Path.Combine(request.Folder, "tmp", "frame-cache.bin"), "x");
        }

        public Task RenderAsync(EngineRenderRequest request, IProgress<EngineEvent>? progress, CancellationToken cancellationToken)
        {
            if (FailRenders-- > 0)
            {
                throw new EngineException("TypeError: x is undefined") { Code = "render" };
            }

            Renders++;
            File.WriteAllBytes(request.Output, new byte[2048]);
            progress?.Report(new EngineEvent(1, "done", Done: true));
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeMedia : IMediaTools
    {
        public IReadOnlyList<string> RequiredComponents { get; } = [];

        /// <summary>The fades of every mux, in order.</summary>
        public List<IReadOnlyList<long>> Fades { get; } = [];

        public Task MuxAsync(string folder, string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, string? assFile, IReadOnlyList<long> fadesMs,
            string output, CancellationToken cancellationToken)
        {
            Fades.Add(fadesMs);
            File.Copy(silentVideo, output, overwrite: true);
            return Task.CompletedTask;
        }

        public bool FailThumbnail { get; init; }

        public Task<bool> ThumbnailAsync(string video, string jpeg, CancellationToken cancellationToken) =>
            FailThumbnail ? throw new IOException("disk full") : Task.FromResult(false);
    }

    internal sealed class FakeVoice : TMConcept.Core.Speech.ISpeechEngine
    {
        public string Code => "kokoro";

        public string Name => "Fake voice";

        public bool IsLocal => true;

        public bool Ready => true;

        public bool Supports(string language) => language == ContentLanguages.English;

        public IReadOnlyList<TMConcept.Core.Speech.SpeechVoice> Voices(string language) => [new("v", "Voice")];

        public Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

        public List<TMConcept.Core.Speech.SpeechRequest> Requests { get; } = [];

        public Task<TMConcept.Core.Speech.SpeechClip> SynthesizeAsync(TMConcept.Core.Speech.SpeechRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            TMConcept.Core.Speech.WaveFile.WritePcm16(request.OutputWav, new float[2400], 24_000);
            return Task.FromResult(new TMConcept.Core.Speech.SpeechClip(request.OutputWav, 100, request.Text.Length, 0, Code));
        }
    }
}
