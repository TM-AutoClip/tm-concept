using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Engines;
using TMConcept.Core.Images;
using TMConcept.Core.Keys;
using TMConcept.Core.Media;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;
using static TMConcept.Tests.Desktop.VideoPageTests;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.49 (K15) on the video page: F10 the plan preview (a template without it is made as before; with it the video waits after its plan
/// before any image is paid for; notes go into one new plan call; night mode does not wait; a rejection pays for no image; one scene first),
/// F16 the checklist (a required gap turns "Produce" off) and the notes on a made video ("Fix": one call, a new version).
/// </summary>
public sealed class PlanReviewPageTests
{
    private const string Content = "[on screen: a map]\nIn 672 the city was under siege.\n[on screen: burning ships]\nA strange fire burned on the water.";

    private const string PlanAnswer = """
        {"scenes":[
          {"scene":1,"base":"title","title":"672","region":[],"nodes":[],"links":[],"anchors":[],"beats":[{"sentence":1,"focus":"","zoom":1,"actions":[]}]},
          {"scene":2,"base":"diagram","title":"","region":[],"nodes":[{"id":"fire","label":"Fire","x":0.5,"y":0.4}],"links":[],"anchors":[],
           "beats":[{"sentence":1,"focus":"","zoom":1,"actions":[]}]}
        ]}
        """;

    private static ChatResponse Plan() => new(PlanAnswer, new ChatUsage(900, 0, 0, 700), null, "end_turn", "r");

    private static ChatResponse Prompts() => new("{\"prompts\":[\"a map\",\"burning ships\"]}", new ChatUsage(100, 0, 0, 50), null, "end_turn", "r");

    private sealed class FakeSheet : IPlanSheet
    {
        public List<IReadOnlyList<double>> Moments { get; } = [];

        public Task<bool> SheetAsync(string video, IReadOnlyList<double> seconds, int columns, string png, CancellationToken cancellationToken)
        {
            Moments.Add(seconds);
            Directory.CreateDirectory(Path.GetDirectoryName(png)!);
            File.WriteAllBytes(png, TMConcept.Core.Videos.PlaceholderImage.Png(8, 8));
            return Task.FromResult(true);
        }
    }

    private static DesktopHarness Harness(FakeChat chat, FakeEngine engine, FakeImages? images = null, FakeSheet? sheet = null) => new(s =>
    {
        s.AddSingleton<IChatClientFactory>(chat);
        s.RemoveAll<IVideoEngine>();
        s.AddSingleton<IVideoEngine>(engine);
        s.AddSingleton<IMediaTools, FakeMedia>();
        s.AddSingleton<IPlanSheet>(sheet ?? new FakeSheet());
        s.AddSingleton<IConfirmDialog>(new FakeConfirm());
        if (images is not null)
        {
            s.RemoveAll<IImageEngine>();
            s.AddSingleton<IImageEngine>(images);
        }
    });

    private static async Task<(VideoViewModel Page, Concept Concept)> OpenAsync(DesktopHarness h, Action<VideoStyle> template, bool images = false,
        bool chooseStyle = true)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "k", Secret = "fake-test-key-review-0123" }, CancellationToken.None);
        var concept = new Concept { ProfileId = profile.Id, Title = "Greek fire", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Approved };
        await h.Get<IConceptStore>().AddAsync([concept], CancellationToken.None);
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice
        {
            Step = PipelineSteps.VideoCode,
            ProfileId = profile.Id,
            Provider = "anthropic",
            Model = "claude-sonnet-5-5",
        }, CancellationToken.None);
        var style = new VideoStyle { Name = "Drawn", Drawer = SceneDrawer.App };
        template(style);
        await h.Get<IVideoStyleStore>().SaveAsync(style, CancellationToken.None);
        var page = h.Get<VideoViewModel>();
        await page.ActivateAsync();
        if (images)
        {
            await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "fal", Label = "k", Secret = "fake-test-key-review-4567" }, CancellationToken.None);
            await page.ActivateAsync();
            var imageRow = page.Picker.Row(PipelineSteps.Images)!;
            imageRow.Selected = imageRow.Options.First(o => o.Model?.Id == "fal-ai/flux-2/flash");
        }

        if (chooseStyle)
        {
            page.SelectedStyle = page.Styles.Single(s => s.Id == style.Id);
        }

        await page.EnqueueAsync(page.Approved.Single());
        return (page, concept);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        condition().ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task ATemplateWithoutThePreviewIsMadeAsBefore()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Plan());
        var sheet = new FakeSheet();
        using var h = Harness(chat, engine, sheet: sheet);
        var (page, concept) = await OpenAsync(h, _ => { });
        var waited = false;
        page.Review.PropertyChanged += (_, e) => waited |= e.PropertyName == nameof(PlanReviewViewModel.Waiting) && page.Review.Waiting;

        await page.ProduceAllAsync();

        chat.Requests.ShouldHaveSingleItem("one plan call, as before");
        engine.Renders.ShouldBe(1, "no preview render");
        sheet.Moments.ShouldBeEmpty();
        waited.ShouldBeFalse();
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldHaveSingleItem();
    }

    [AvaloniaFact]
    public async Task ThePlanWaitsForApprovalBeforeAnyImageIsPaidFor()
    {
        var engine = new FakeEngine();
        var images = new FakeImages();
        var sheet = new FakeSheet();
        var chat = new FakeChat().Answer(_ => Prompts()).Answer(_ => Plan());
        using var h = Harness(chat, engine, images, sheet);
        var (page, concept) = await OpenAsync(h, s => s.PlanReview = true, images: true);

        var run = page.ProduceAllAsync();
        await Until(() => page.Review.Waiting);

        // Waiting: the plan was paid (and the image prompts), no image yet; the page is free, the queue item is running.
        images.Prompts.ShouldBeEmpty("no image is paid for before the plan is approved");
        chat.Requests.Count.ShouldBe(2);
        page.Busy.ShouldBeTrue();
        page.Review.Scenes.Select(s => s.Number).ShouldBe([1, 2]);
        page.Review.HasSheet.ShouldBeTrue();
        sheet.Moments.ShouldHaveSingleItem().Count.ShouldBe(2, "one still per beat");
        engine.Renders.ShouldBe(1, "the preview render");

        page.Review.Approve();
        await run;

        page.Review.Waiting.ShouldBeFalse();
        images.Prompts.Count.ShouldBe(2, "both images after the approval");
        engine.Renders.ShouldBe(2);
        chat.Requests.Count.ShouldBe(2, "approving costs no call");
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldHaveSingleItem();
    }

    [AvaloniaFact]
    public async Task AllTheNotesGoIntoOneNewPlanCallAndTheNewPlanIsShownAgain()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Plan()).Answer(_ => Plan());
        using var h = Harness(chat, engine);
        var (page, _) = await OpenAsync(h, s => s.PlanReview = true);

        var run = page.ProduceAllAsync();
        await Until(() => page.Review.Waiting);
        page.Review.Scenes[0].Note = "bigger title";
        page.Review.Scenes[1].Note = "show the fleet";
        page.Review.Revise();
        await Until(() => chat.Requests.Count == 2 && page.Review.Waiting && page.Review.Title.Contains('2', StringComparison.Ordinal));

        var again = chat.Requests[1].User;
        again.ShouldContain("Scene 1: «bigger title»");
        again.ShouldContain("Scene 2: «show the fleet»");
        again.ShouldContain("\"diagram\"", Case.Sensitive, "the plan the user saw goes with the notes");
        page.Review.Approve();
        await run;
        chat.Requests.Count.ShouldBe(2, "two notes, one call");
        engine.Renders.ShouldBe(3, "two previews and the video");
    }

    [AvaloniaFact]
    public async Task NightModeDoesNotWaitForThePlan()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Plan());
        using var h = Harness(chat, engine);
        var (page, concept) = await OpenAsync(h, s => s.PlanReview = true);
        page.NightMode = true;

        await page.ProduceAllAsync();

        page.Review.Waiting.ShouldBeFalse();
        engine.Renders.ShouldBe(1, "no preview");
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldHaveSingleItem();
    }

    [AvaloniaFact]
    public async Task NightModeSwitchedOnDuringTheWaitLetsTheVideoGoOn()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Plan()), engine);
        var (page, concept) = await OpenAsync(h, s => s.PlanReview = true);

        var run = page.ProduceAllAsync();
        await Until(() => page.Review.Waiting);
        page.NightMode = true;
        await run;

        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldHaveSingleItem();
    }

    [AvaloniaFact]
    public async Task ARejectedPlanPaysForNoImageAndKeepsTheConceptQueued()
    {
        var engine = new FakeEngine();
        var images = new FakeImages();
        using var h = Harness(new FakeChat().Answer(_ => Prompts()).Answer(_ => Plan()), engine, images);
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        var (page, concept) = await OpenAsync(h, s => s.PlanReview = true, images: true);

        var run = page.ProduceAllAsync();
        await Until(() => page.Review.Waiting);
        page.Review.Reject();
        await run;

        images.Prompts.ShouldBeEmpty();
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldBeEmpty();
        page.Queue.Single().Error.ShouldStartWith(text["video.error.rejected"]);
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Queued);
    }

    [AvaloniaFact]
    public async Task OneSceneFirstMakesTheFirstImageAndWaitsOnceMore()
    {
        var engine = new FakeEngine();
        var images = new FakeImages();
        using var h = Harness(new FakeChat().Answer(_ => Prompts()).Answer(_ => Plan()), engine, images);
        var (page, concept) = await OpenAsync(h, s =>
        {
            s.PlanReview = true;
            s.FirstSceneReview = true;
        }, images: true);

        var run = page.ProduceAllAsync();
        await Until(() => page.Review.Waiting);
        page.Review.FirstScene.ShouldBeFalse();
        page.Review.Approve();
        await Until(() => page.Review.Waiting && page.Review.FirstScene);

        images.Prompts.ShouldBe(["a map"], "only the first scene's image so far");
        page.Review.Scenes.ShouldHaveSingleItem().Number.ShouldBe(1);
        page.Review.CanRevise.ShouldBeFalse();
        page.Review.Approve();
        await run;

        images.Prompts.ShouldBe(["a map", "burning ships"], "the first image is not made twice");
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldHaveSingleItem();
    }

    [AvaloniaFact]
    public async Task ProduceIsOffWhileARequiredGapIsOpen()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat(), engine);
        var (page, _) = await OpenAsync(h, _ => { }, chooseStyle: false);

        await page.CheckAsync();
        page.CanProduce.ShouldBeFalse("no template");
        page.Checklist.Rows.Where(r => r.Required).Select(r => r.Gap.Code).ShouldBe([ProductionGaps.Template]);
        page.Checklist.Rows.Single(r => r.Gap.Code == ProductionGaps.Template).CanSkip.ShouldBeFalse();

        page.SelectedStyle = page.Styles.Single();
        await page.CheckAsync();
        page.CanProduce.ShouldBeTrue();
        page.Checklist.Rows.ShouldNotContain(r => r.Required);

        // An optional gap never turns it off; skipped, it is no longer listed.
        var voice = page.Checklist.Rows.Single(r => r.Gap.Code == ProductionGaps.Voice);
        page.Checklist.SkipCommand.Execute(voice);
        page.Checklist.Rows.ShouldNotContain(r => r.Gap.Code == ProductionGaps.Voice);
        page.CanProduce.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task ANoteBelongsToItsSceneAndFixMakesANewVersionWithOneCall()
    {
        var engine = new FakeEngine();
        const string fix = """
            {"scenes":[{"scene":2,"narration":"The fire burned even on water.","image":"","base":"diagram","title":"","region":[],
              "nodes":[{"id":"fire","label":"Fire","x":0.5,"y":0.4}],"links":[],"anchors":[],"beats":[{"sentence":1,"focus":"","zoom":1,"actions":[]}]}]}
            """;
        var chat = new FakeChat().Answer(_ => Plan()).Answer(_ => new ChatResponse(fix, new ChatUsage(500, 0, 0, 300), null, "end_turn", "r"));
        using var h = Harness(chat, engine);
        var (page, concept) = await OpenAsync(h, _ => { });
        await page.ProduceAllAsync();
        var record = (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single();
        var profile = h.Get<AppSession>().Profile!;
        var source = h.Get<VideoRemaker>().Load(record);

        var notes = h.Get<TimeNotesViewModel>();
        await notes.LoadAsync(record, profile);
        notes.Ready.ShouldBeTrue();
        notes.Begin((long)(source.Input.Scenes[1].StartSeconds * 1000) + 300);
        notes.Begin(0);
        notes.Draft = "say what burned";
        await notes.AddAsync();
        var note = notes.Notes.ShouldHaveSingleItem();
        note.Note.Scene.ShouldBe(2, "the moment writing started decides the scene");
        File.ReadAllText(Path.Combine(source.Folder, VideoFolders.NotesFile)).ShouldContain("say what burned");
        notes.CanFix.ShouldBeTrue();

        VideoRecord? announced = null;
        notes.Made += (_, record) => announced = record;
        await notes.FixAsync();

        notes.Status.ShouldNotBeEmpty();
        chat.Requests.Count.ShouldBe(2, "one call for the notes");
        chat.Requests[1].System.ShouldStartWith("You fix scenes");
        chat.Requests[1].User.ShouldContain("Viewer's note: «say what burned»");
        chat.Requests[1].User.ShouldNotContain("Scene 1 (");
        announced.ShouldNotBeNull();
        var versions = await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None);
        versions.Count.ShouldBe(2);
        var made = versions.Single(v => v.Id != record.Id);
        var input = h.Get<VideoRemaker>().Load(made).Input;
        input.Scenes[1].Caption.ShouldBe("The fire burned even on water.");
        input.Scenes[1].Plan.ShouldNotBeNull().Base.ShouldBe(TMConcept.Core.Plans.PlanBases.Diagram);
        announced!.Id.ShouldBe(made.Id);
        input.Scenes[0].Caption.ShouldBe(source.Input.Scenes[0].Caption, "a scene without a note is not changed");
        Directory.Exists(source.Folder).ShouldBeTrue("the old version is kept");
    }
}
