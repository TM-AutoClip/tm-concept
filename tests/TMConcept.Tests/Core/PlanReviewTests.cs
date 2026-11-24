using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Engines;
using TMConcept.Core.Models;
using TMConcept.Core.Plans;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;
using TMConcept.Providers.Catalog;
using TMConcept.Tests.Desktop;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.49 (K15): F10 the plan preview's rules (when a video waits, the key moments, the small copy it renders, the placeholder, the revise
/// prompt, the estimate's extra plan call) and F16 the notes on a made video (moment → scene, the fix answer) and the checklist's rule table.
/// </summary>
public sealed class PlanReviewTests
{
    private sealed class Review(bool night) : IPlanReview
    {
        public bool NightMode => night;

        public Task<PlanReviewAnswer> PlanAsync(PlanPreview preview, CancellationToken cancellationToken) => Task.FromResult(PlanReviewAnswer.Approve);

        public Task<bool> FirstSceneAsync(PlanPreview preview, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private static RenderInput Input()
    {
        var input = RenderInputBuilder.Build("Greek fire", "en",
            [new RenderSceneSource("a map", "In 672 the city was under siege.", "scene-1.png", 4000, string.Empty),
                new RenderSceneSource("ships", "A strange fire burned. It burned on the water.", string.Empty, 6000, string.Empty)], 1080, 1920, 30);
        input.Scenes[0].Plan = new RenderPlan { Base = PlanBases.Image, Beats = [new PlanBeat { Sentence = 1, Start = 0, Duration = 4 }] };
        input.Scenes[1].Plan = new RenderPlan
        {
            Base = PlanBases.Diagram,
            Beats = [new PlanBeat { Sentence = 1, Start = input.Scenes[1].StartSeconds, Duration = 3 },
                new PlanBeat { Sentence = 2, Start = input.Scenes[1].StartSeconds + 3, Duration = 0.5 }],
        };
        return input;
    }

    [Fact]
    public void OnlyAnAppDrawnTemplateWithThePreviewOnWaitsAndNeverInNightMode()
    {
        var on = new VideoStyle { Drawer = SceneDrawer.App, PlanReview = true };
        PlanReviewRules.Waits(EngineKind.HyperFrames, on, new Review(false)).ShouldBeTrue();
        PlanReviewRules.Waits(EngineKind.HyperFrames, new VideoStyle { Drawer = SceneDrawer.App }, new Review(false)).ShouldBeFalse("off by default");
        PlanReviewRules.Waits(EngineKind.HyperFrames, new VideoStyle { Drawer = SceneDrawer.FreeCode, PlanReview = true }, new Review(false))
            .ShouldBeFalse("only the app's drawer has a plan");
        PlanReviewRules.Waits(EngineKind.Remotion, on, new Review(false)).ShouldBeFalse();
        PlanReviewRules.Waits(EngineKind.HyperFrames, on, new Review(true)).ShouldBeFalse("night mode: nobody is there to answer");
        PlanReviewRules.Waits(EngineKind.HyperFrames, on, null).ShouldBeFalse("nobody to answer");
        PlanReviewRules.Waits(EngineKind.HyperFrames, null, new Review(false)).ShouldBeFalse();
        new VideoStyle().PlanReview.ShouldBeFalse();
        new VideoStyle().FirstSceneReview.ShouldBeFalse();

        PlanReviewRules.FirstScene(EngineKind.HyperFrames, on, new Review(false)).ShouldBeFalse();
        var first = new VideoStyle { Drawer = SceneDrawer.App, PlanReview = true, FirstSceneReview = true };
        PlanReviewRules.FirstScene(EngineKind.HyperFrames, first, new Review(false)).ShouldBeTrue();
        PlanReviewRules.FirstScene(EngineKind.HyperFrames, first, new Review(true)).ShouldBeFalse();
        PlanReviewRules.FirstScene(EngineKind.HyperFrames, new VideoStyle { Drawer = SceneDrawer.App, FirstSceneReview = true }, new Review(false))
            .ShouldBeFalse("one scene first only with the plan preview");
    }

    [Fact]
    public void EveryBeatHasAKeyMomentAndManyAreChosenEvenly()
    {
        var input = Input();
        var moments = PlanReviewRules.KeyMoments(input);
        moments.Count.ShouldBe(3);
        moments[0].ShouldBe(1.5, 0.001, "60 % of a 4 s beat, at most 1.5 s in");
        moments[1].ShouldBe(input.Scenes[1].StartSeconds + 1.5, 0.001);
        moments[2].ShouldBe(input.Scenes[1].StartSeconds + 3.3, 0.001);
        moments.ShouldAllBe(m => m >= 0 && m < input.DurationSeconds);

        input.Scenes[1].Plan = null;
        PlanReviewRules.KeyMoments(input)[1].ShouldBe(input.Scenes[1].StartSeconds + (input.Scenes[1].DurationSeconds / 2), 0.001, "no beats: the middle");

        var many = Input();
        many.Scenes[1].Plan!.Beats = Enumerable.Range(0, 40).Select(i => new PlanBeat { Start = many.Scenes[1].StartSeconds + (i * 0.1), Duration = 0.1 }).ToList();
        var chosen = PlanReviewRules.KeyMoments(many, 5);
        chosen.Count.ShouldBe(5);
        chosen.ShouldBe(chosen.OrderBy(m => m).ToList());
    }

    [Fact]
    public void ThePreviewRendersASmallSlowCopyAndLeavesTheInputAsItWas()
    {
        var input = Input();
        var small = PlanReviewRules.PreviewInput(input);
        (small.Width, small.Height).ShouldBe((360, 640));
        small.Fps.ShouldBe(PlanPreviewLimits.Fps);
        small.DurationSeconds.ShouldBe(input.DurationSeconds);
        small.TotalFrames.ShouldBe((int)Math.Round(input.DurationSeconds * PlanPreviewLimits.Fps));
        small.Scenes[1].StartSeconds.ShouldBe(input.Scenes[1].StartSeconds);
        small.Scenes[1].Plan!.Beats.Count.ShouldBe(2, "the plans come along");
        (input.Width, input.Fps).ShouldBe((1080, 30), "the video's own input is not changed");

        var first = PlanReviewRules.PreviewInput(input, 1);
        first.Scenes.ShouldHaveSingleItem().Number.ShouldBe(1);
        first.DurationSeconds.ShouldBe(input.Scenes[0].DurationSeconds, 0.001);
        PlanReviewRules.Scenes(input).Select(s => s.Base).ShouldBe([PlanBases.Image, PlanBases.Diagram]);
    }

    [Fact]
    public void APlaceholderIsAGreyPngOfTheImagesSize()
    {
        var png = PlaceholderImage.Png(64, 36);
        png[..8].ShouldBe(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)).ShouldBe(64);
        System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)).ShouldBe(36);
        System.Text.Encoding.ASCII.GetString(png, png.Length - 8, 4).ShouldBe("IEND");
    }

    [Fact]
    public void AllTheNotesGoIntoOneRevisePromptWithThePlanTheUserSaw()
    {
        var user = PlanPromptBuilder.Revise("SCENES", "{\"scenes\":[]}", [new SceneNote(2, "show the fleet"), new SceneNote(1, "closer «map»"), new SceneNote(3, " ")]);
        user.ShouldStartWith("SCENES");
        user.ShouldContain("{\"scenes\":[]}");
        user.IndexOf("Scene 1: «closer \"map\"»", StringComparison.Ordinal).ShouldBeLessThan(user.IndexOf("Scene 2: «show the fleet»", StringComparison.Ordinal));
        user.ShouldNotContain("Scene 3:", Case.Sensitive, "an empty note is not sent");
        user.ShouldEndWith("keep every scene without a note as it was.");
    }

    [Fact]
    public void ThePreviewAddsOnePlanCallToTheHighEndOnlyWhenItIsOn()
    {
        var catalog = new EmbeddedModelCatalog();
        var code = catalog.Find("anthropic", "claude-sonnet-5-5").ShouldNotBeNull();
        var estimator = new VideoCostEstimator(new CostCalculator(), catalog);
        var profile = new Profile { Name = "P" };
        var concept = new Concept { ProfileId = profile.Id, Title = "Greek fire", Summary = "s", Content = "[on screen: a map]\nIn 672 the city was under siege.", Language = "en" };
        var engine = new VideoPageTests.FakeEngine();
        CostEstimate Of(VideoStyle style) => estimator.Estimate([concept], profile, engine, code, null, null, true, style);

        var off = Of(new VideoStyle { Drawer = SceneDrawer.App });
        var on = Of(new VideoStyle { Drawer = SceneDrawer.App, PlanReview = true });
        on.MinUsd.ShouldBe(off.MinUsd, "no note, no extra call");
        on.MaxUsd.ShouldBeGreaterThan(off.MaxUsd);
        Of(new VideoStyle { Drawer = SceneDrawer.FreeCode, PlanReview = true }).ShouldBe(Of(new VideoStyle { Drawer = SceneDrawer.FreeCode }),
            "free code has no plan to preview");
    }

    [Fact]
    public void ANoteBelongsToTheScenePlayingAtItsMoment()
    {
        var input = Input();
        var second = (long)(input.Scenes[1].StartSeconds * 1000);
        TimeNotes.SceneAt(input, 0).ShouldBe(1);
        TimeNotes.SceneAt(input, second - 1).ShouldBe(1);
        TimeNotes.SceneAt(input, second).ShouldBe(2);
        TimeNotes.SceneAt(input, 999_999).ShouldBe(2, "past the end: the last scene");
        TimeNotes.SceneAt(input, -5).ShouldBe(1);
        TimeNotes.SceneAt(new RenderInput(), 10).ShouldBe(0);

        var notes = TimeNotes.ByScene([
            new TimeNote { AtMs = 9000, Scene = 2, Text = "later" }, new TimeNote { AtMs = 5000, Scene = 2, Text = "first" },
            new TimeNote { AtMs = 100, Scene = 1, Text = "slower" }, new TimeNote { AtMs = 200, Scene = 1, Text = "  " }]);
        notes.ShouldBe([new SceneNote(1, "slower"), new SceneNote(2, "first / later")]);
    }

    [Fact]
    public async Task TheNotesAreKeptInTheVersionFolder()
    {
        var folder = TestFolders.New("tmc-notes-");
        Directory.CreateDirectory(folder);
        var notes = new[] { new TimeNote { AtMs = 4200, Scene = 2, Text = "Şehir daha büyük" }, new TimeNote { AtMs = 100, Scene = 1, Text = "a" } };
        await TimeNotes.WriteAsync(folder, notes, CancellationToken.None);
        File.ReadAllText(Path.Combine(folder, VideoFolders.NotesFile)).ShouldContain("Şehir daha büyük");
        (await TimeNotes.ReadAsync(folder, CancellationToken.None)).Select(n => n.AtMs).ShouldBe([100L, 4200L]);
        await TimeNotes.WriteAsync(folder, [], CancellationToken.None);
        File.Exists(Path.Combine(folder, VideoFolders.NotesFile)).ShouldBeFalse();
        (await TimeNotes.ReadAsync(folder, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public void TheFixAnswerGivesEachNotedSceneItsTextAndPicture()
    {
        var read = SceneFixPrompt.Read("{\"scenes\":[{\"scene\":2,\"narration\":\" New words. \",\"image\":\"\"},{\"scene\":0,\"narration\":\"x\",\"image\":\"y\"}]}");
        read.Keys.ShouldBe([2]);
        read[2].ShouldBe(("New words.", string.Empty));
        SceneFixPrompt.Read("not json").ShouldBeEmpty();
        var user = SceneFixPrompt.User(Input(), ["one", "two"], [new SceneNote(2, "say it slower")], plans: true);
        user.ShouldContain("Scene 2");
        user.ShouldNotContain("Scene 1 (");
        user.ShouldContain("Viewer's note: «say it slower»");
        user.ShouldContain("\"base\":\"diagram\"", Case.Sensitive, "a drawn video's plan is sent with its scene");
        SceneFixPrompt.Schema(true)["properties"]!["scenes"]!["items"]!["required"]!.AsArray().Select(n => n!.GetValue<string>())
            .ShouldContain("narration");
        SceneFixer.Noted(Input(), [new SceneNote(5, "x"), new SceneNote(2, "y"), new SceneNote(1, " ")]).ShouldBe([new SceneNote(2, "y")]);
    }

    // The checklist's rule table: every gap shows when its fact is missing and not when it is there.
    [Theory]
    [InlineData(ProductionGaps.Template, GapKind.Required)]
    [InlineData(ProductionGaps.CodeModel, GapKind.Required)]
    [InlineData(ProductionGaps.Key, GapKind.Required)]
    [InlineData(ProductionGaps.Voice, GapKind.Optional)]
    [InlineData(ProductionGaps.MusicMissing, GapKind.Optional)]
    [InlineData(ProductionGaps.MusicUnreadable, GapKind.Optional)]
    [InlineData(ProductionGaps.Pronunciation, GapKind.Optional)]
    public void EachGapShowsOnlyWhenItsPieceIsMissing(string code, GapKind kind)
    {
        var complete = Facts();
        ProductionChecklist.Gaps(complete).ShouldBeEmpty("the defaults have no gap");
        var missing = code switch
        {
            ProductionGaps.Template => Facts(template: false),
            ProductionGaps.CodeModel => Facts(codeModel: false),
            ProductionGaps.Key => Facts(withoutKey: ["fal"]),
            ProductionGaps.Voice => Facts(voice: false),
            ProductionGaps.MusicMissing => Facts(ownMusic: true, exists: false),
            ProductionGaps.MusicUnreadable => Facts(ownMusic: true, exists: true, readable: false),
            _ => Facts(dictionaryEmpty: true),
        };
        var gap = ProductionChecklist.Gaps(missing).ShouldHaveSingleItem();
        (gap.Code, gap.Kind).ShouldBe((code, kind));
        ProductionChecklist.Blocks([gap]).ShouldBe(kind == GapKind.Required, "only a required gap turns Produce off");
        ProductionChecklist.Open([gap], new HashSet<string> { ProductionChecklist.Key(gap) }).Count.ShouldBe(kind == GapKind.Required ? 1 : 0,
            "a required gap cannot be skipped");
    }

    [Fact]
    public void FineDetailsOfTheChecklist()
    {
        ProductionChecklist.Gaps(Facts(ownMusic: true, exists: true, readable: null)).ShouldBeEmpty("not checked yet (no ffmpeg): read when the video starts");
        ProductionChecklist.Gaps(Facts(voice: false, dictionaryEmpty: true)).Select(g => g.Code).ShouldBe([ProductionGaps.Voice], "no voice, no dictionary note");
        var gaps = ProductionChecklist.Gaps(Facts(template: false, codeModel: false, voice: false, withoutKey: ["fal", "fal", "openai"]));
        gaps.Select(g => g.Code + g.Detail).ShouldBe(["template", "code-model", "keyfal", "keyopenai", "voice"]);

        // A skipped music gap makes this video's copy of the template silent; the template itself and an unskipped gap keep the music.
        var music = Facts(ownMusic: true, exists: false);
        var skipped = new HashSet<string> { ProductionGaps.MusicMissing };
        var snapshot = new VideoStyle { Music = new VideoMusic { Source = MusicSource.OwnFile, File = "x.mp3" } };
        ProductionChecklist.WithoutMusic(snapshot, ProductionChecklist.Gaps(music), new HashSet<string>()).ShouldBeFalse();
        snapshot.Music.Source.ShouldBe(MusicSource.OwnFile);
        ProductionChecklist.WithoutMusic(snapshot, ProductionChecklist.Gaps(Facts()), skipped).ShouldBeFalse("the file is back: the music stays");
        ProductionChecklist.WithoutMusic(snapshot, ProductionChecklist.Gaps(music), skipped).ShouldBeTrue();
        snapshot.Music.Source.ShouldBe(MusicSource.Off);
    }

    private static ChecklistFacts Facts(bool template = true, bool codeModel = true, IReadOnlyList<string>? withoutKey = null, bool voice = true,
        bool ownMusic = false, bool exists = false, bool? readable = true, bool dictionaryEmpty = false) => new()
        {
            Template = template,
            CodeModel = codeModel,
            ProvidersWithoutKey = withoutKey ?? [],
            Voice = voice,
            OwnMusic = ownMusic,
            MusicExists = exists,
            MusicReadable = readable,
            PronunciationEmpty = dictionaryEmpty,
        };
}
