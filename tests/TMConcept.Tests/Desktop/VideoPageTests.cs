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
using TMConcept.Core.Styles;
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

    /// <summary>
    /// A profile with a key, its video code choice and one approved concept; the page is open (nothing queued) with a default template chosen
    /// (0.1.19: nothing is produced without one), unless <paramref name="chooseStyle"/> is false.
    /// </summary>
    private static async Task<(VideoViewModel Page, Concept Concept)> OpenAsync(DesktopHarness h, bool chooseStyle = true, Action<VideoStyle>? template = null)
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
        var style = new VideoStyle { Name = "Default" };
        template?.Invoke(style);
        await h.Get<IVideoStyleStore>().SaveAsync(style, CancellationToken.None);
        var page = h.Get<VideoViewModel>();
        await page.ActivateAsync();
        if (chooseStyle)
        {
            page.SelectedStyle = page.Styles.Single(s => s.Id == style.Id);
        }

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

    private const string PlanAnswer = """
        {"scenes":[
          {"scene":1,"base":"map","title":"","region":[20,30,45,45],"nodes":[],"links":[],
           "anchors":[{"id":"city","label":"Constantinople","find":"","lon":28.97,"lat":41.0,"x":null,"y":null},
                      {"id":"sea","label":"Marmara","find":"","lon":28.0,"lat":40.7,"x":null,"y":null}],
           "beats":[{"sentence":1,"focus":"city","zoom":1.6,"actions":[{"type":"pin","from":"","to":"","via":[],"at":"city","label":"","text":"","walker":"","dashed":false,"country":"","size":0,"fromValue":null,"value":null,"suffix":""},
                                                              {"type":"teleport","from":"","to":"","via":[],"at":"city","label":"","text":"","walker":"","dashed":false,"country":"","size":0,"fromValue":null,"value":null,"suffix":""}]}]},
          {"scene":2,"base":"diagram","title":"","region":[],"nodes":[{"id":"fire","label":"Fire","x":0.5,"y":0.4}],"links":[],"anchors":[],
           "beats":[{"sentence":1,"focus":"","zoom":1,"actions":[{"type":"highlight","from":"","to":"","via":[],"at":"fire","label":"","text":"","walker":"","dashed":false,"country":"","size":0.1,"fromValue":null,"value":null,"suffix":""}]}]}
        ]}
        """;

    [AvaloniaFact]
    public async Task ATemplateDrawnByTheAppAsksForAScenePlanInsteadOfCodeAndRendersTheDrawer()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => new ChatResponse(PlanAnswer, new ChatUsage(900, 0, 0, 700), null, "end_turn", "r"));
        using var h = Harness(chat, engine);
        var (page, concept) = await OpenAsync(h, template: s => s.Drawer = SceneDrawer.App);
        await page.EnqueueAsync(page.Approved.Single());

        await page.ProduceAllAsync();

        // One paid call, the plan (never code); the app's drawer is the scene file and every scene has its cleaned, timed plan.
        var request = chat.Requests.ShouldHaveSingleItem();
        request.System.ShouldStartWith("You plan an animated explainer video");
        request.User.ShouldContain("Scene 1 (");
        request.User.ShouldContain("1) «In 672 the city was under siege.»");
        var scenes = engine.Input!.Scenes;
        scenes[0].Plan.ShouldNotBeNull().Base.ShouldBe(TMConcept.Core.Plans.PlanBases.Map);
        scenes[0].Plan!.Beats.Single().Actions.Select(a => a.Type).ShouldBe(["pin"], "an unknown action is dropped");
        scenes[0].Plan!.Beats.Single().Start.ShouldBe(scenes[0].StartSeconds, 0.01);
        scenes[1].Plan.ShouldNotBeNull().Base.ShouldBe(TMConcept.Core.Plans.PlanBases.Diagram);
        scenes[1].Plan!.Beats.Single().Start.ShouldBe(scenes[1].StartSeconds, 0.01);
        var video = (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single();
        video.Warnings.ShouldNotContain(VideoWarnings.AppCode);
        video.TotalUsd.ShouldBeGreaterThan(0);
        var folder = LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, video.Folder);
        TMConcept.Core.Plans.PlanDrawer.IsDrawer(File.ReadAllText(Path.Combine(folder, VideoFolders.Source, engine.CodeFile))).ShouldBeTrue();
        File.ReadAllText(Path.Combine(folder, VideoFolders.Source, "input.json")).ShouldContain("\"plan\"");
    }

    [AvaloniaFact]
    public async Task MotionCheckFindingsAreYellowWarningsWithAFrameStripAndNeverStopTheVideo()
    {
        // 0.1.41 (F3): black frames inside the film are a yellow warning (badge code, activity line) with a strip in checks/; old strips go.
        var engine = new FakeEngine();
        var motion = new FakeMotion();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)).Answer(_ => Code(engine.SampleCode)), engine,
            s => s.AddSingleton<IMotionFrames>(motion));
        var (page, concept) = await PrepareAsync(h);
        await page.ProduceAllAsync();

        var video = (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single();
        video.Warnings.Split(',').ShouldContain(VideoWarnings.MotionBlack);
        var folder = LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, video.Folder);
        var strip = motion.Strips.ShouldHaveSingleItem();
        Path.GetDirectoryName(strip).ShouldBe(Path.Combine(folder, VideoFolders.Checks));
        File.Exists(strip).ShouldBeTrue();
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        h.Get<TMConcept.Core.Activity.IActivityFeed>().Recent()
            .ShouldContain(l => l.Text == text.Format("video." + VideoWarnings.MotionBlack, "Greek fire"));

        // A check that cannot run is skipped: the video is still made, without a motion warning and without strips.
        motion.Fail = true;
        var second = await RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = false, Images = ImageSources.None });
        second.Warnings.Split(',').ShouldNotContain(VideoWarnings.MotionBlack);
        Directory.Exists(Path.Combine(LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, second.Folder), VideoFolders.Checks)).ShouldBeFalse();
        motion.Strips.Count.ShouldBe(1);
    }

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
        page.Picker.Rows.Select(r => r.Step.Code).ShouldBe([PipelineSteps.VideoCode, PipelineSteps.Images, PipelineSteps.ImagePoints, PipelineSteps.Speech, PipelineSteps.Concepts]);
        var line = Find<Avalonia.Controls.TextBlock>(view, "SummaryLine").Text!;
        foreach (var part in new[] { "video-code", "engine", "images", "image-points", "speech", "concepts" })
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
    public async Task ATemplateWithoutSubtitlesChosenAfterQueueingReachesTheItemAndTheCodeGetsNoNarrationToDraw()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine);
        var (page, concept) = await PrepareAsync(h);
        page.SelectedStyle!.Subtitles.ShouldBeTrue("a new template has subtitles on");

        // Another template chosen after the item was queued: the choice of the moment the item starts applies, and it is kept on this computer.
        var silent = new VideoStyle { Name = "No subtitles", Subtitles = false, ProgressBar = true, PaletteId = "ocean" };
        await h.Get<IVideoStyleStore>().SaveAsync(silent, CancellationToken.None);
        await page.ActivateAsync();
        page.SelectedStyle = page.Styles.Single(s => s.Id == silent.Id);
        h.Get<TMConcept.Desktop.Paths.UiPreferencesFile>().Read().VideoStyleId.ShouldBe(silent.Id);
        await page.ProduceAllAsync();

        var video = (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single();
        var options = VideoRemaker.Options(video);
        options.Subtitles.ShouldBeFalse("the video records its choice; its versions follow it");
        var snapshot = options.Style.ShouldNotBeNull("the video keeps a copy of its template");
        (snapshot.Id, snapshot.Name, snapshot.Subtitles, snapshot.ProgressBar, snapshot.PaletteId).ShouldBe((silent.Id, "No subtitles", false, true, "ocean"));
        var request = chat.Requests.Single();
        request.System.ShouldContain("There are NO subtitles");
        request.System.ShouldContain("NEVER draw the caption (narration) text");
        request.User.ShouldNotContain("siege", Case.Insensitive, "no narration in the prompt");
        request.User.ShouldNotContain("caption");
        engine.Input!.Scenes.ShouldAllBe(s => s.Caption.Length == 0, "the code reads no narration at render time");
        // The overlay carries only the app-drawn progress bar of this template: no subtitle line is burned in.
        var burned = ((FakeMedia)h.Get<IMediaTools>()).Overlays.Single().AssText.ShouldNotBeNull("the template's progress bar is drawn by the app");
        burned.ShouldContain(@"\clip(");
        burned.ShouldNotContain("siege", Case.Insensitive, "no subtitles burned in");
        burned.Split('\n').ShouldNotContain(l => l.StartsWith("Dialogue:", StringComparison.Ordinal) && l.Contains(",Default,", StringComparison.Ordinal));

        // The kept input, the .srt and the narration file keep the real narration (a later version and the Produced page read them).
        var folder = LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, video.Folder);
        var kept = TMConcept.Core.Render.RenderInputBuilder.FromJson(File.ReadAllText(Path.Combine(folder, VideoFolders.Source, VideoFolders.InputFile)))!;
        kept.Scenes[0].Caption.ShouldBe("In 672 the city was under siege.");
        File.ReadAllText(Path.Combine(folder, VideoFolders.SubtitlesFile)).ShouldContain("siege");
        File.ReadAllText(Path.Combine(folder, VideoFolders.NarrationFile)).ShouldContain("siege");
    }

    /// <summary>
    /// End to end (0.1.19): the template stored in Settings, picked on the page, reaches the render input (palette tokens and theme, font,
    /// title), the project (font files), the prompt (template block) and the mux (progress bar and closing card in the overlay, the channel
    /// photo saved with the profile in the database placed on the card).
    /// </summary>
    [AvaloniaFact]
    public async Task ATemplatePickedOnThePageReachesTheRenderInputTheFontsTheOverlayAndTheChannelPhoto()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine);
        var (page, concept) = await OpenAsync(h, chooseStyle: false);

        // The channel photo is a copy in the library, stored on the profile (the database column of Version0119).
        var library = h.Get<IAppPaths>().LibraryDirectory;
        var photo = Path.Combine(library, "Byzantium", "channel", "0123456789-me.png");
        Directory.CreateDirectory(Path.GetDirectoryName(photo)!);
        await File.WriteAllBytesAsync(photo, [0x89, 0x50, 0x4E, 0x47]);
        var profile = (await h.Get<IProfileStore>().GetAsync(concept.ProfileId, CancellationToken.None)).ShouldNotBeNull();
        profile.ChannelPhoto = LibraryPaths.Relative(library, photo);
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);

        var style = new VideoStyle
        {
            Name = "Ocean",
            Subtitles = true,
            ProgressBar = true,
            FontId = "poppins",
            PaletteId = "ocean",
            TitleStyle = TitleStyle.Boxed,
            TitlePosition = TitlePosition.MiddleCenter,
            Closing = ClosingPlacement.Screen,
            ClosingPlatform = ClosingPlatform.YouTube,
        };
        await h.Get<IVideoStyleStore>().SaveAsync(style, CancellationToken.None);
        await page.ActivateAsync();
        page.SelectedStyle = page.Styles.Single(s => s.Id == style.Id);
        await page.EnqueueAsync(page.Approved.Single());
        await page.ProduceAllAsync();

        page.Produced.Single().Title.ShouldBe("Greek fire");
        var input = engine.Input.ShouldNotBeNull();
        var rendered = input.Style.ShouldNotBeNull("the template reaches the render input");
        rendered.Colors.Background.ShouldBe("#06202B");
        rendered.Colors.Accent.ShouldBe("#2EC4F1");
        input.Theme.Background.ShouldBe("#06202B", "the theme follows the palette");
        rendered.Font.ShouldBe("Poppins");
        rendered.FontRegular.ShouldBe("fonts/Poppins-Regular.ttf");
        rendered.TitleStyle.ShouldNotBeNullOrEmpty();
        rendered.Subtitles.ShouldBeTrue();
        rendered.ProgressBar.ShouldBeTrue();
        rendered.EndCardSeconds.ShouldBeGreaterThan(0, "a closing card on screen");
        engine.Fonts.ShouldContain("Poppins-Regular.ttf", "the font files are copied into the project");
        chat.Requests.Single().System.ShouldContain("Poppins");

        var mux = ((FakeMedia)h.Get<IMediaTools>()).Overlays.Single();
        mux.AssText.ShouldNotBeNull().ShouldContain("Poppins", Case.Sensitive, "subtitles in the template font");
        mux.AssText.ShouldContain("Byzantium", Case.Sensitive, "the closing card names the channel");
        mux.Files.ShouldContain("fonts/Poppins-Regular.ttf");
        var placed = mux.Overlay.Photo.ShouldNotBeNull("the channel photo is placed on the closing card");
        mux.Files.ShouldContain(placed.File);
        placed.StartSeconds.ShouldBeGreaterThan(0);
    }

    [AvaloniaFact]
    public async Task TheTemplateChoiceIsKeptAcrossRestartsAndEachVideoKeepsTheCopyItStartedWith()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine);

        // As after a restart: the kept choice is what the page shows when it opens.
        var (page, concept) = await OpenAsync(h, chooseStyle: false);
        page.SelectedStyle.ShouldBeNull();
        var kept = new VideoStyle { Name = "Kept", Subtitles = true, FontId = "poppins" };
        await h.Get<IVideoStyleStore>().SaveAsync(kept, CancellationToken.None);
        h.Get<TMConcept.Desktop.Paths.UiPreferencesFile>().Change(p => p.VideoStyleId = kept.Id);
        await page.ActivateAsync();
        page.Styles.Select(s => s.Name).ShouldBe(["Default", "Kept"]);
        page.SelectedStyle.ShouldNotBeNull().Id.ShouldBe(kept.Id);
        await page.EnqueueAsync(page.Approved.Single());

        await page.ProduceAllAsync();

        chat.Requests.Single().User.ShouldContain("caption «In 672 the city was under siege.»");
        chat.Requests.Single().System.ShouldContain("Subtitles are burned into the bottom");
        engine.Input!.Scenes[0].Caption.ShouldBe("In 672 the city was under siege.");
        ((FakeMedia)h.Get<IMediaTools>()).Ass.ShouldBe(["subtitles.ass"]);

        // The template changes in Settings after the video was made: the video keeps the copy it started with.
        kept.Subtitles = false;
        kept.FontId = "anton";
        await h.Get<IVideoStyleStore>().SaveAsync(kept, CancellationToken.None);
        var video = (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single();
        var snapshot = VideoRemaker.Options(video).Style.ShouldNotBeNull();
        (snapshot.Subtitles, snapshot.FontId).ShouldBe((true, "poppins"));
        VideoRemaker.Options(video).Subtitles.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task NothingIsProducedUntilACodeModelAndATemplateAreChosen()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine);
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        var (page, concept) = await OpenAsync(h, chooseStyle: false);
        page.SelectedStyle.ShouldBeNull("a new install has no template chosen");
        h.Get<TMConcept.Desktop.Paths.UiPreferencesFile>().Read().VideoStyleId.ShouldBeNull();
        await page.EnqueueAsync(page.Approved.Single());

        await page.ProduceAllAsync();
        page.Status.ShouldBe(text["video.style.none"]);
        h.Get<TMConcept.Core.Activity.IActivityFeed>().Recent()[^1].Text.ShouldBe(text["video.style.none"]);
        await page.RetryAsync(page.Queue.Single());
        page.Status.ShouldBe(text["video.style.none"]);

        // Without a code model either, both are asked for.
        await h.Get<IStepModelStore>().ClearAsync(PipelineSteps.VideoCode, concept.ProfileId, CancellationToken.None);
        await page.ActivateAsync();
        page.Picker.Model(PipelineSteps.VideoCode).ShouldBeNull();
        await page.ProduceAllAsync();
        page.Status.ShouldBe(text["video.style.both"]);
        chat.Requests.ShouldBeEmpty("nothing was asked or paid for");
        page.Queue.Single().Error.ShouldBeNullOrEmpty();
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task ADeletedTemplateIsNoLongerChosenAndAQueueThatStillHadItSaysWhy()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine);
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        var (page, concept) = await PrepareAsync(h);
        var chosen = page.SelectedStyle.ShouldNotBeNull();

        // Deleted in Settings while this page still shows it: the item fails before anything is paid.
        await h.Get<IVideoStyleStore>().DeleteAsync(chosen.Id, CancellationToken.None);
        await page.ProduceAllAsync();
        page.Queue.Single().Error.ShouldContain(text["video.style.gone"]);
        chat.Requests.ShouldBeEmpty();
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldBeEmpty();

        // Shown again: nothing is chosen and the kept choice is forgotten.
        await page.ActivateAsync();
        page.SelectedStyle.ShouldBeNull();
        page.Styles.ShouldBeEmpty();
        page.NoStyles.ShouldBeTrue();
        h.Get<TMConcept.Desktop.Paths.UiPreferencesFile>().Read().VideoStyleId.ShouldBeNull();
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

        // The item keeps no settings: the models and the subtitles choice are read when it starts.
        (await h.Get<IQueueStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single().ConceptId.ShouldBe(concept.Id);
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
    public async Task AVideoWhoseCodeModelLostItsKeyStopsTheQueueWithARedLineAndANotification()
    {
        // Night mode off: the key is gone when the item starts (removed after the queue was prepared). The queue stops on it, and the
        // video's own red line and notification say why (0.1.18 before this fix: a red line only, no notification).
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        var notes = new RecordingNotifier();
        using var h = Harness(chat, engine, s =>
        {
            s.AddSingleton<TMConcept.Core.Notifications.INotifier>(notes);
            s.AddSingleton<IVideoQueueRunner>(p => new KeyGoneRunner(p.GetRequiredService<IApiKeyStore>()));
        });
        var (page, _) = await PrepareAsync(h);
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        var row = page.Queue.Single();

        await page.ProduceAllAsync();

        row.Error.ShouldBe(text.Format("concepts.no_key", "anthropic"));
        chat.Requests.ShouldBeEmpty();
        var failed = notes.Sent.ShouldHaveSingleItem();
        failed.Kind.ShouldBe(TMConcept.Core.Notifications.NotificationKind.Failed);
        failed.Title.ShouldBe(text["video.failed.title"]);
        failed.Message.ShouldContain(row.Error);
        h.Get<TMConcept.Core.Activity.IActivityFeed>().Recent().ShouldContain(l => l.IsError && l.Text.Contains(row.Error, StringComparison.Ordinal));
        page.Status.ShouldBe(text["video.queue.stopped.error"]);
    }

    [AvaloniaFact]
    public async Task EachMadeVideoAppearsUnderProducedWhileTheQueueStillRuns()
    {
        // The developer (18 Nov 2026): the made videos showed only after a page switch; the page skipped its own change event while busy.
        var engine = new FakeEngine();
        var watch = new ListWatchingRunner();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)).Answer(_ => Code(engine.SampleCode)), engine,
            s => s.AddSingleton<IVideoQueueRunner>(watch));
        var (page, concept) = await OpenAsync(h);
        await h.Get<IConceptStore>().AddAsync([new Concept
        {
            ProfileId = concept.ProfileId, Title = "Iron chain", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Approved,
        }], CancellationToken.None);
        await page.ActivateAsync();
        foreach (var card in page.Approved.ToList())
        {
            await page.EnqueueAsync(card);
        }

        watch.Lists = () => (page.Produced.Count, page.Queue.Count);
        await page.ProduceAllAsync();

        watch.Seen.ShouldBe([(1, 1), (2, 0)], "after each video: one more made, one less queued, before the next one starts");
    }

    /// <summary>The real runner; after each item it notes how many videos the page lists as made and queued.</summary>
    private sealed class ListWatchingRunner : IVideoQueueRunner
    {
        public Func<(int Produced, int Queued)>? Lists { get; set; }

        public List<(int Produced, int Queued)> Seen { get; } = [];

        public Task<QueueRunResult> RunAsync<T>(Func<CancellationToken, Task<T?>> next, Func<T, CancellationToken, Task<QueueItemOutcome>> produce,
            Func<bool> nightMode, CancellationToken cancellationToken)
            where T : class =>
            new VideoQueueRunner(TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<VideoQueueRunner>.Instance).RunAsync(next,
                async (item, token) =>
                {
                    var outcome = await produce(item, token);
                    Seen.Add(Lists!());
                    return outcome;
                }, nightMode, cancellationToken);
    }

    /// <summary>The real runner, after removing every API key (as if the user deleted the key while the queue was being prepared).</summary>
    private sealed class KeyGoneRunner(IApiKeyStore keys) : IVideoQueueRunner
    {
        public async Task<QueueRunResult> RunAsync<T>(Func<CancellationToken, Task<T?>> next, Func<T, CancellationToken, Task<QueueItemOutcome>> produce,
            Func<bool> nightMode, CancellationToken cancellationToken)
            where T : class
        {
            foreach (var key in await keys.ListAsync(null, cancellationToken))
            {
                await keys.DeleteAsync(key.Id, cancellationToken);
            }

            return await new VideoQueueRunner(TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<VideoQueueRunner>.Instance)
                .RunAsync(next, produce, nightMode, cancellationToken);
        }
    }

    /// <summary>Keeps every notification.</summary>
    private sealed class RecordingNotifier : TMConcept.Core.Notifications.INotifier
    {
        public List<TMConcept.Core.Notifications.AppNotification> Sent { get; } = [];

        public void Notify(TMConcept.Core.Notifications.AppNotification notification) => Sent.Add(notification);
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
        // (0.1.15: a vertical video's subtitle line holds 26 characters, so "under" and "siege" may stand on two lines.)
        var subtitles = File.ReadAllText(Path.Combine(folder, "subtitles.srt"));
        subtitles.ShouldContain("siege.");
        subtitles.ShouldNotContain("seej");
        File.ReadAllText(Path.Combine(folder, VideoFolders.NarrationFile)).ShouldNotContain("seej");
        record.SpeechModel.ShouldStartWith(ModelProviders.Local + "/" + voice.Code);
    }

    [AvaloniaFact]
    public async Task TheSpeedChosenOnThePageReachesEveryVoiceRequest()
    {
        // The developer (18 Nov 2026): the chosen speed did not seem to reach the video. Picker -> saved choice -> narration -> the voice's request.
        var engine = new FakeEngine();
        var voice = new FakeVoice();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine, s =>
        {
            s.RemoveAll<TMConcept.Core.Speech.ISpeechEngine>();
            s.AddSingleton<TMConcept.Core.Speech.ISpeechEngine>(voice);
        });
        var (page, concept) = await PrepareAsync(h);
        var row = page.Picker.Row(PipelineSteps.Speech)!;
        row.Selected = row.Options.First(o => o.Model?.Id == voice.Code);
        row.Speed = 1.3m;
        await page.Picker.SaveAsync();

        var saved = await h.Get<IStepModelStore>().GetAsync(PipelineSteps.Speech, concept.ProfileId, CancellationToken.None);
        StepModelResolver.Options(saved).Speed.ShouldBe(1.3, 1e-9);
        await RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = true });

        voice.Requests.Count.ShouldBe(2, "one request per scene");
        voice.Requests.ShouldAllBe(r => Math.Abs(r.Speed - 1.3) < 1e-9);
    }

    [AvaloniaFact]
    public async Task AVoiceWithoutASpeedOfItsOwnIsStretchedToTheChosenSpeed()
    {
        var engine = new FakeEngine();
        var voice = new FakeVoice { HandlesSpeed = false, Samples = 24_000 };
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine, s =>
        {
            s.RemoveAll<TMConcept.Core.Speech.ISpeechEngine>();
            s.AddSingleton<TMConcept.Core.Speech.ISpeechEngine>(voice);
        });
        var (_, concept) = await PrepareAsync(h);
        var options = StepModelResolver.OptionsJson(new StepOptions { Speed = 1.5 });
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice { Step = PipelineSteps.Speech, ProfileId = concept.ProfileId, Provider = ModelProviders.Local,
            Model = voice.Code, OptionsJson = options }, CancellationToken.None);

        var record = await RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = true });

        var folder = LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, record.Folder);
        var clips = Directory.GetFiles(Path.Combine(folder, VideoFolders.Audio), "*.wav");
        // Every sentence is one second spoken at 1.5 times its speed (about 667 ms); the natural tempo adds its sentence silence between the
        // sentences of a scene and its scene silence after each scene (0.1.31).
        var pacing = TMConcept.Core.Speech.NarrationTempos.Of(TMConcept.Core.Speech.NarrationTempo.Natural);
        var sentences = voice.Requests.Count;
        var expected = (sentences * 667) + ((sentences - clips.Length) * pacing.SentenceGapMs) + (clips.Length * pacing.SceneGapMs);
        clips.Sum(TMConcept.Core.Speech.WaveFile.DurationMs).ShouldBeInRange(expected - (25 * sentences), expected + (25 * sentences));
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
    public async Task ALocalCodeModelWhoseCodeFailsTheCheckGetsTheAppsOwnAnimationAtOnceAndAnOnlineOneStillFails()
    {
        // 0.1.23, the developer's full local run: a small local model failed the check twice and no video was made. 20 Nov 2026: its repair
        // copied the example rather than fixing the design, so a local model gets no repair: the engine's own code is rendered after the
        // first failed check. The check is the same: an online model's code that fails twice still stops the video.
        var engine = new FakeEngine();
        const string Bad = "(function(){const t={};document.body.innerHTML=\"<b>x</b>\";})();";
        var chat = new FakeChat().Answer(_ => Code(Bad));
        using var h = Harness(chat, engine);
        var (_, concept) = await PrepareAsync(h);
        var local = new ResolvedModel(TMConcept.Core.AppOllama.AppModelRegistry.Entry(TMConcept.Core.AppOllama.AppModelAdvisor.Find("qwen3.5:4b-q8_0")!),
            new ApiKey { Provider = ModelProviders.AppOllama, Label = string.Empty, Secret = string.Empty }, string.Empty);

        var record = await RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = false }, local);

        chat.Requests.ShouldHaveSingleItem().System.ShouldContain("Checklist before you answer");
        record.Warnings.ShouldContain(VideoWarnings.AppCode);
        record.Warnings.ShouldNotContain(VideoWarnings.Repaired);
        var folder = LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, record.Folder);
        File.ReadAllText(Path.Combine(folder, VideoFolders.Source, engine.CodeFile)).Trim().ShouldBe(((IVideoEngine)engine).FallbackCode);
        h.Get<TMConcept.Desktop.Localization.Localizer>()["produced.note." + VideoWarnings.AppCode].ShouldNotStartWith("produced.note.");

        var second = new Concept { ProfileId = concept.ProfileId, Title = "Iron chain", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Approved };
        await h.Get<IConceptStore>().AddAsync([second], CancellationToken.None);
        chat.Answer(_ => Code(Bad)).Answer(_ => Code(Bad));
        var online = await Should.ThrowAsync<VideoException>(() => RunAsync(h, second, new VideoOptions { Engine = engine.Kind, Speech = false }));
        online.Code.ShouldBe("code");
        chat.Requests[1].System.ShouldNotContain("Checklist");
        chat.Requests[2].User.ShouldContain("Fix: create every element with document.createElement");
        chat.Requests[2].User.ShouldContain(engine.SampleCode, Case.Sensitive, "the online repair shows the engine's minimal file");
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
    public async Task AMadeVideoCarriesTheAiEditsOfItsConceptOnceOutsideItsOwnTotal()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)).Answer(_ => Code(engine.SampleCode)), engine);
        var (_, concept) = await PrepareAsync(h);
        var store = h.Get<TMConcept.Core.Runs.IRunStore>();
        TMConcept.Core.Runs.Run Edit(Guid? conceptId, decimal usd, bool exact = true) => new()
        {
            ProfileId = concept.ProfileId, ConceptId = conceptId, Kind = TMConcept.Core.Runs.RunKind.ConceptEdit, ActualUsd = usd, CostExact = exact,
            Status = TMConcept.Core.Runs.RunStatus.Succeeded,
        };

        // Two paid edits of this concept (one canceled, so its cost is estimated), a free one (a local model), an edit of another concept
        // and an edit from before 0.1.25 that names no concept: only this concept's paid edits are its cost.
        var first = Edit(concept.Id, 0.03m);
        var canceled = Edit(concept.Id, 0.01m, exact: false);
        foreach (var run in new[] { first, canceled, Edit(concept.Id, 0), Edit(Guid.NewGuid(), 0.5m), Edit(null, 0.7m) })
        {
            await store.AddAsync(run, CancellationToken.None);
        }

        var record = await RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = false });

        var costs = await h.Get<IVideoStore>().CostsAsync(record.Id, CancellationToken.None);
        var edits = costs.Where(c => c.Step == VideoCostBreakdown.ConceptEdits).ToList();
        edits.Select(c => (c.RunId, c.Usd, c.Exact)).ShouldBe([((Guid?)first.Id, 0.03m, true), (canceled.Id, 0.01m, false)], ignoreOrder: true);
        VideoCostBreakdown.EditsUsd(costs).ShouldBe(0.04m);
        record.TotalUsd.ShouldBe(costs.Where(c => c.Step != VideoCostBreakdown.ConceptEdits).Sum(c => c.Usd), "the edit runs count in the month once");

        // A second video of the same concept records only the edits made after the first one.
        var later = Edit(concept.Id, 0.02m);
        await store.AddAsync(later, CancellationToken.None);
        (await store.ConceptEditsNotInVideosAsync(concept.Id, CancellationToken.None)).Select(r => r.Id).ShouldContain(later.Id);
        (await store.ConceptEditsNotInVideosAsync(concept.Id, CancellationToken.None)).Select(r => r.Id).ShouldNotContain(first.Id);
        var again = await RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = false });
        (await h.Get<IVideoStore>().CostsAsync(again.Id, CancellationToken.None)).Where(c => c.Step == VideoCostBreakdown.ConceptEdits)
            .Select(c => c.RunId).ShouldBe([(Guid?)later.Id]);
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
    public async Task ANarrationModelThatCannotBeResolvedStopsTheVideoBeforeTheCodeCall()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine);
        var (_, concept) = await PrepareAsync(h);

        // Kokoro speaks English but is not installed in the test folder: resolving the chosen voice fails before anything is spoken. Since
        // 16 Nov 2026 that stops the video (0.1.15 made it silent): nothing is paid for a video the user did not ask for.
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice { Step = PipelineSteps.Speech, ProfileId = concept.ProfileId, Provider = ModelProviders.Local,
            Model = "kokoro" }, CancellationToken.None);

        var failure = await Should.ThrowAsync<VideoException>(() => RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = true }));

        failure.Code.ShouldBe("speech");
        failure.Message.ShouldContain("not installed");
        chat.Requests.ShouldBeEmpty("the code model is never called");
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task AVoiceThatFailsWhileSpeakingStopsTheVideo()
    {
        var engine = new FakeEngine();
        var voice = new FakeVoice { Fail = true };
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine, s =>
        {
            s.RemoveAll<TMConcept.Core.Speech.ISpeechEngine>();
            s.AddSingleton<TMConcept.Core.Speech.ISpeechEngine>(voice);
        });
        var (_, concept) = await PrepareAsync(h);
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice { Step = PipelineSteps.Speech, ProfileId = concept.ProfileId, Provider = ModelProviders.Local,
            Model = voice.Code }, CancellationToken.None);

        var failure = await Should.ThrowAsync<VideoException>(() => RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = true }));

        (failure.Code, failure.Message).ShouldBe(("speech", "fake voice crashed"));
        chat.Requests.ShouldBeEmpty("no silent video and no paid code call after a failed voice");
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Queued);
    }

    [AvaloniaFact]
    public async Task AnImageEngineFailureStopsTheVideoBeforeTheCodeCallAndSaysWhy()
    {
        var engine = new FakeEngine();
        var images = new FakeImages { Fail = "z-image-turbo could not make the image (sd-cli -1073741515): DLL not found" };
        var chat = new FakeChat().Answer(_ => new ChatResponse("{\"prompts\":[\"a map\",\"burning ships\"]}", new ChatUsage(100, 0, 0, 50), null, "end_turn", "r"))
            .Answer(_ => Code(engine.SampleCode));
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

        await page.ProduceAllAsync();

        // Only the image prompts were asked for: the video code model was never called (0.1.15 went on without images and paid for it).
        chat.Requests.ShouldHaveSingleItem().System.ShouldBe(ImagePromptBuilder.System);
        row.Failed.ShouldBeTrue();
        row.Error.ShouldContain(text["video.error.images"]);
        row.Error.ShouldContain("sd-cli -1073741515");
        row.Progress!.Stages.Single(s => s.Code == VideoSteps.Images).State.ShouldBe(StageState.Failed);
        row.Progress.Stages.Single(s => s.Code == VideoSteps.Code).State.ShouldBe(StageState.Waiting);
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldBeEmpty();
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Queued);

        // Said where the user looks: a red activity line with the engine's message and a Failed notification.
        var lines = h.Get<TMConcept.Core.Activity.IActivityFeed>().Recent();
        lines.ShouldContain(l => l.IsError && l.Text.Contains("sd-cli -1073741515", StringComparison.Ordinal) && l.Text.Contains("Greek fire", StringComparison.Ordinal));
        var toast = h.Get<TMConcept.Desktop.Notifications.ToastNotifier>().Open.Last().DataContext as TMConcept.Desktop.Notifications.ToastViewModel;
        toast!.Message.ShouldContain("sd-cli -1073741515");
    }

    [AvaloniaFact]
    public async Task WithNightModeOffAFailedVideoStopsTheWholeQueue()
    {
        var engine = new FakeEngine();
        var images = new FakeImages { Unready = new ImageEngineProblem(ImageEngineProblems.VisualCppRuntime, "vcruntime140_1.dll") };
        var chat = new FakeChat();
        using var h = Harness(chat, engine, s =>
        {
            s.RemoveAll<IImageEngine>();
            s.AddSingleton<IImageEngine>(images);
        });
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        var (page, concept) = await OpenAsync(h);
        var second = new Concept
        {
            ProfileId = concept.ProfileId, Title = "Hagia Sophia", Summary = "s", Content = concept.Content, Language = "en", Status = ConceptStatus.Approved,
        };
        await h.Get<IConceptStore>().AddAsync([second], CancellationToken.None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "fal", Label = "k", Secret = "fake-test-key-video-4567" }, CancellationToken.None);
        await page.ActivateAsync();
        var imageRow = page.Picker.Row(PipelineSteps.Images)!;
        imageRow.Selected = imageRow.Options.First(o => o.Model?.Id == "fal-ai/flux-2/flash");
        foreach (var card in page.Approved.ToList())
        {
            await page.EnqueueAsync(card);
        }

        page.NightMode.ShouldBeFalse("night mode is off at every start");
        await page.ProduceAllAsync();

        page.Queue.Count.ShouldBe(2);
        page.Queue.Count(r => r.Failed).ShouldBe(1, "the queue stopped at the first failure; the second video was not started");
        page.Status.ShouldBe(text["video.queue.stopped.error"]);
    }

    [AvaloniaFact]
    public async Task AnImageEngineThatCannotRunHereStopsTheVideoBeforeThePaidPromptCall()
    {
        var engine = new FakeEngine();
        var images = new FakeImages { Unready = new ImageEngineProblem(ImageEngineProblems.VisualCppRuntime, "vcruntime140_1.dll, msvcp140.dll") };
        var chat = new FakeChat().Answer(_ => new ChatResponse("{\"prompts\":[\"a map\",\"burning ships\"]}", new ChatUsage(100, 0, 0, 50), null, "end_turn", "r"))
            .Answer(_ => Code(engine.SampleCode));
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
        var before = h.Get<TMConcept.Core.Activity.IActivityFeed>().Recent().Count;

        await page.ProduceAllAsync();

        // Nothing was asked of any model: the image prompts are a paid call (0.1.16 still paid for it before sd-cli failed).
        chat.Requests.ShouldBeEmpty();
        images.Prompts.ShouldBeEmpty();
        row.Failed.ShouldBeTrue();
        row.Error.ShouldContain(text["video.error.image_runtime"]);
        row.Error.ShouldContain("vcruntime140_1.dll, msvcp140.dll");
        row.Progress!.Stages.Single(s => s.Code == VideoSteps.Prepare).State.ShouldBe(StageState.Failed);
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Queued);

        // One red line in the user's language, not the pipeline's and the assembler's log lines as well (the 0.1.16 review).
        var lines = h.Get<TMConcept.Core.Activity.IActivityFeed>().Recent().Skip(before).Where(l => l.IsError).ToList();
        lines.ShouldHaveSingleItem().Text.ShouldContain(text["video.error.image_runtime"]);
    }

    [AvaloniaFact]
    public async Task ACutOffImagePromptAnswerGetsOneRepairAndTheVideoIsMade()
    {
        var engine = new FakeEngine();
        var images = new FakeImages();
        var chat = new FakeChat().Answer(_ => throw new TMConcept.Core.Providers.ProviderException("Fake: the answer was cut off.")
            {
                Code = "truncated",
                ChargedUsage = new ChatUsage(100, 0, 0, 4000),
                PartialText = "{\"prompts\":[{\"scene\":1,\"prompt\":\"a map\"},{\"scene\":2,\"prompt\":\"burn",
            })
            .Answer(_ => new ChatResponse("{\"prompts\":[{\"scene\":1,\"prompt\":\"a map\"},{\"scene\":2,\"prompt\":\"burning ships\"}]}",
                new ChatUsage(100, 0, 0, 50), null, "end_turn", "r"))
            .Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine, s =>
        {
            s.RemoveAll<IImageEngine>();
            s.AddSingleton<IImageEngine>(images);
        });
        var (page, concept) = await OpenAsync(h);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "fal", Label = "k", Secret = "fake-test-key-video-4567" }, CancellationToken.None);
        await page.ActivateAsync();
        var imageRow = page.Picker.Row(PipelineSteps.Images)!;
        imageRow.Selected = imageRow.Options.First(o => o.Model?.Id == "fal-ai/flux-2/flash");
        await page.EnqueueAsync(page.Approved.Single());
        var row = page.Queue.Single();

        await page.ProduceAllAsync();

        // The cut is not "the video code was cut off": its complete prompt is kept, the one repair names the missing scene, the code step runs.
        row.Failed.ShouldBeFalse(row.Error);
        chat.Requests.Count.ShouldBe(3);
        chat.Requests[1].User.ShouldContain("Your previous answer did not fit the scenes: 1 prompts for 2 scenes; scene 2 has no prompt.");
        images.Prompts.ShouldBe(["a map", "burning ships"]);
        var made = (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldHaveSingleItem();
        made.TotalUsd.ShouldBeGreaterThan(0m, "the cut-off prompt answer was paid for and is counted");
    }

    [AvaloniaFact]
    public async Task AnImageModelThatIsNotReadyStopsTheVideoAtPrepare()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine);
        var (_, concept) = await PrepareAsync(h);

        // An image model of a provider without a key: the step cannot be made ready.
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice { Step = PipelineSteps.Images, ProfileId = concept.ProfileId, Provider = "fal",
            Model = "fal-ai/flux-2/flash" }, CancellationToken.None);
        var reports = new System.Collections.Concurrent.ConcurrentQueue<VideoProgress>();

        var failure = await Should.ThrowAsync<VideoException>(() => RunAsync(h, concept,
            new VideoOptions { Engine = engine.Kind, Speech = false, Images = ImageSources.Model }, new ForwardProgress<VideoProgress>(reports.Enqueue)));

        failure.Code.ShouldBe("images");
        chat.Requests.ShouldBeEmpty();
        reports.Last().Step.ShouldBe(VideoSteps.Prepare, "the Prepare row shows the failure");
    }

    [AvaloniaFact]
    public async Task WarningsGatheredBeforeAFailureAreStillShown()
    {
        var engine = new FakeEngine { FailRenders = 2 };
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode)).Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine);
        var (page, concept) = await OpenAsync(h);
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();

        // Narration off: an expected situation stays a warning; the render then fails twice.
        var direct = await Should.ThrowAsync<VideoException>(() => RunAsync(h, concept,
            new VideoOptions { Engine = engine.Kind, Speech = false, Images = ImageSources.None }));
        direct.Warnings.ShouldBe([VideoWarnings.SpeechOff]);

        engine.FailRenders = 2;
        chat.Answer(_ => Code(engine.SampleCode)).Answer(_ => Code(engine.SampleCode));
        await page.ActivateAsync();
        var speechRow = page.Picker.Row(PipelineSteps.Speech)!;
        speechRow.Selected = speechRow.Options[0];
        await page.EnqueueAsync(page.Approved.Single());
        await page.ProduceAllAsync();

        page.Queue.Single().Failed.ShouldBeTrue();
        var lines = h.Get<TMConcept.Core.Activity.IActivityFeed>().Recent();
        var title = page.Queue.Single().Title;
        lines.ShouldContain(l => l.IsWarning && l.Text == text.Format("video.failed.noted", title, text["video.noted.speech_off"]));
    }

    private static Task<VideoRecord> RunAsync(DesktopHarness h, Concept concept, VideoOptions options, IProgress<VideoProgress>? progress = null) =>
        RunAsync(h, concept, options, new ResolvedModel(h.Get<IModelCatalog>().Find("anthropic", "claude-sonnet-5-5")!,
            new ApiKey { Provider = "anthropic", Label = "k", Secret = "fake-test-key-video-0123" }, string.Empty), progress);

    private static async Task<VideoRecord> RunAsync(DesktopHarness h, Concept concept, VideoOptions options, ResolvedModel model,
        IProgress<VideoProgress>? progress = null)
    {
        var profile = (await h.Get<IProfileStore>().GetAsync(concept.ProfileId, CancellationToken.None))!;
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
            queue.AddAsync(new QueueItem { ProfileId = first.ProfileId, ConceptId = third.Id, Position = 9 },
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

        /// <summary>When set, every image fails with this message (as sd-cli's exit does).</summary>
        public string? Fail { get; init; }

        /// <summary>What the engine reports against running here (a missing Visual C++ runtime, a missing driver).</summary>
        public ImageEngineProblem? Unready { get; init; }

        public ImageEngineProblem? Problem(ResolvedModel model) => Unready;

        public Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken cancellationToken)
        {
            if (Fail is not null)
            {
                throw new InvalidOperationException(Fail);
            }

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

        /// <summary>The font files in the project when it was last written (the template's font, placed by the app).</summary>
        public IReadOnlyList<string> Fonts { get; private set; } = [];

        public EngineKind Kind { get; init; } = EngineKind.HyperFrames;

        public string Name { get; init; } = "Fake";

        public int Renders { get; private set; }

        public bool Experimental => false;

        public bool NeedsLicenseConsent => false;

        public string CodeFile => "scene.js";

        public string Contract => "contract";

        public string SampleCode => "window.__timelines = window.__timelines || {}; window.__timelines[\"concept\"] = gsap.timeline({ paused: true });\n"
            + "window.INPUT.scenes.forEach(function (scene) { const picture = document.createElement(\"img\"); picture.src = scene.image; });";

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
            Fonts = Directory.Exists(Path.Combine(request.Folder, "fonts")) ? Directory.GetFiles(Path.Combine(request.Folder, "fonts")).Select(f => Path.GetFileName(f)!).ToList() : [];
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

        /// <summary>The subtitle file burned in at every mux, in order (null = none).</summary>
        public List<string?> Ass { get; } = [];

        /// <summary>
        /// What every mux drew over the video, in order: the overlay, its ASS text as it was in the work folder (null = none) and the files
        /// there at that moment (the font folder's as "fonts/&lt;name&gt;").
        /// </summary>
        public List<(MuxOverlay Overlay, string? AssText, IReadOnlyList<string> Files)> Overlays { get; } = [];

        public Task MuxAsync(string folder, string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, MuxOverlay overlay, IReadOnlyList<long> fadesMs,
            string output, CancellationToken cancellationToken)
        {
            Fades.Add(fadesMs);
            Ass.Add(overlay.AssFile);
            var files = Directory.GetFiles(folder).Select(f => Path.GetFileName(f)!).ToList();
            if (overlay.FontsFolder is { } fonts && Directory.Exists(Path.Combine(folder, fonts)))
            {
                files.AddRange(Directory.GetFiles(Path.Combine(folder, fonts)).Select(f => fonts + "/" + Path.GetFileName(f)));
            }

            Overlays.Add((overlay, overlay.AssFile is null ? null : File.ReadAllText(Path.Combine(folder, overlay.AssFile)), files));
            File.Copy(silentVideo, output, overwrite: true);
            return Task.CompletedTask;
        }

        public bool FailThumbnail { get; init; }

        /// <summary>The length ffmpeg reads from a music file (0.1.44); null = a damaged file.</summary>
        public long? MusicMs { get; set; } = 5000;

        /// <summary>The music files read, by name.</summary>
        public List<string> MusicRead { get; } = [];

        public Task<long?> AudioDurationAsync(string file, CancellationToken cancellationToken)
        {
            MusicRead.Add(Path.GetFileName(file));
            return Task.FromResult(MusicMs);
        }

        public Task<bool> ThumbnailAsync(string video, string jpeg, CancellationToken cancellationToken) =>
            FailThumbnail ? throw new IOException("disk full") : Task.FromResult(false);

        /// <summary>Every sample asked for (readable text, 0.1.25): the file's name, the moments and the region.</summary>
        public List<(string File, IReadOnlyList<long> AtMs, FrameRegion Region)> Samples { get; } = [];

        /// <summary>The pixels of a sample (file name, moment); none by default, so the normal readable look is kept.</summary>
        public Func<string, long, FramePixels?> Pixels { get; set; } = (_, _) => null;

        public Task<IReadOnlyList<FramePixels?>> SampleAsync(string file, IReadOnlyList<long> atMs, FrameRegion region, CancellationToken cancellationToken)
        {
            Samples.Add((Path.GetFileName(file), atMs, region));
            return Task.FromResult<IReadOnlyList<FramePixels?>>(atMs.Select(at => Pixels(Path.GetFileName(file), at)).ToList());
        }
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

        public bool Fail { get; init; }

        /// <summary>Whether it speaks at the requested speed itself (false: like Pocket TTS, Chatterbox and the VoxCPM voices).</summary>
        public bool HandlesSpeed { get; init; } = true;

        /// <summary>A clip of this many samples at 24 kHz (a 220 Hz tone when longer than the default silence).</summary>
        public int Samples { get; init; } = 2400;

        public Task<TMConcept.Core.Speech.SpeechClip> SynthesizeAsync(TMConcept.Core.Speech.SpeechRequest request, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new TMConcept.Core.Speech.SpeechException("fake voice crashed");
            }

            Requests.Add(request);
            var samples = new float[Samples];
            if (Samples > 2400)
            {
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = 0.5f * MathF.Sin(2 * MathF.PI * 220 * i / 24_000);
                }
            }

            TMConcept.Core.Speech.WaveFile.WritePcm16(request.OutputWav, samples, 24_000);
            return Task.FromResult(new TMConcept.Core.Speech.SpeechClip(request.OutputWav, Samples * 1000L / 24_000, request.Text.Length, 0, Code));
        }
    }

    /// <summary>The motion check's frames without ffmpeg (0.1.41): three seconds of a lit picture with black frames at 1.0–1.2 s.</summary>
    internal sealed class FakeMotion : IMotionFrames
    {
        public bool Fail { get; set; }

        public List<string> Strips { get; } = [];

        public Task<MotionSeries?> ReadAsync(string video, double fps, string workFolder, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("ffmpeg is not installed");
            }

            var luminance = Enumerable.Range(0, 30).Select(i => i is >= 10 and <= 12 ? 0.0 : 0.5).ToList();
            return Task.FromResult<MotionSeries?>(new MotionSeries(10, luminance, Enumerable.Repeat(0.01, 30).ToList(), Enumerable.Repeat(0.0, 30).ToList()));
        }

        public Task<bool> StripAsync(string video, double atSeconds, double spanSeconds, double fps, string png, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(png)!);
            File.WriteAllBytes(png, [1, 2, 3]);
            Strips.Add(png);
            return Task.FromResult(true);
        }
    }
}
