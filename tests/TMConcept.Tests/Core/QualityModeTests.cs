using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Engines;
using TMConcept.Core.Images;
using TMConcept.Core.Keys;
using TMConcept.Core.Media;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Plans;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;
using TMConcept.Providers.Catalog;
using TMConcept.Tests.Desktop;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.50 (K16): F11 the quality modes (Fast changes nothing; Standard reviews once and scores once without a repair; Careful repairs in rounds
/// with a limit, drops polish after round 2 and stops when the list does not shrink), the reviewer's answers read from recorded samples (no
/// network), the estimate's lines, and F14 the recurring character added once to every image prompt.
/// </summary>
public sealed class QualityModeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tmconcept-quality-" + Guid.NewGuid().ToString("N"));

    /// <summary>A recorded frame review answer: thinking, a code fence, loose severities, a scene that does not exist, a repeat, a fourth finding of one scene.</summary>
    private const string RecordedReview = """
        <think>The sheet has 9 frames. Scene 2's label is cut.</think>
        Here is the review:
        ```json
        {"findings":[
          {"scene":2,"severity":"Blocker","problem":"The label 'Fire' is cut off at the right edge."},
          {"scene":2,"severity":"polish","problem":"The arrow head is slightly blurred."},
          {"scene":"1","severity":"POLISH","problem":"Title text is a little close to the top edge."},
          {"scene":7,"severity":"blocker","problem":"No such scene."},
          {"scene":2,"severity":"blocker","problem":"The label 'Fire' is cut off at the right edge."},
          {"scene":2,"severity":"blocker","problem":"Third problem."},
          {"scene":2,"severity":"blocker","problem":"Fourth problem is one too many."},
          {"scene":1,"severity":"weird","problem":"   "}
        ]}
        ```
        """;

    /// <summary>A recorded critic answer: an unknown heading, a score out of range, a repeated heading, a score as text.</summary>
    private const string RecordedCritic = """
        {"scores":[
          {"heading":"readable text","score":5,"note":"All labels are large."},
          {"heading":"Picture fits the narration","score":3,"note":"Scene 2 shows ships, the narration speaks of fire."},
          {"heading":"Vibes","score":2,"note":"unknown heading"},
          {"heading":"Ending","score":9,"note":"Clear end card."},
          {"heading":"Ending","score":1,"note":"second answer of a heading"},
          {"heading":"Opening","score":"4","note":"Strong first frame."}
        ]}
        """;

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class FakeReviewer : IQualityReviewer
    {
        private readonly Queue<IReadOnlyList<QualityFinding>?> _frames = new();

        private readonly Queue<IReadOnlyList<QualityFinding>?> _pictures = new();

        public int FrameCalls { get; private set; }

        public int PictureCalls { get; private set; }

        public int CriticCalls { get; private set; }

        public int CharacterCalls { get; private set; }

        public List<int> Repairs { get; } = [];

        public IReadOnlyList<QualityFinding> Character { get; set; } = [];

        public FakeReviewer Frames(params IReadOnlyList<QualityFinding>?[] answers)
        {
            foreach (var answer in answers)
            {
                _frames.Enqueue(answer);
            }

            return this;
        }

        public FakeReviewer Pictures(params IReadOnlyList<QualityFinding>?[] answers)
        {
            foreach (var answer in answers)
            {
                _pictures.Enqueue(answer);
            }

            return this;
        }

        public Task<IReadOnlyList<QualityFinding>?> FramesAsync(QualityCall call, string sheet, RenderInput input, IReadOnlyList<QualityFrame> frames, bool subtitles,
            CancellationToken cancellationToken)
        {
            FrameCalls++;
            File.Exists(sheet).ShouldBeTrue("the review sees the sheet");
            return Task.FromResult(_frames.Count > 0 ? _frames.Dequeue() : []);
        }

        public Task<IReadOnlyList<QualityFinding>?> PicturesAsync(QualityCall call, IReadOnlyList<QualityPicture> pictures, int scenes, CancellationToken cancellationToken)
        {
            PictureCalls++;
            return Task.FromResult(_pictures.Count > 0 ? _pictures.Dequeue() : []);
        }

        public Task<IReadOnlyList<QualityFinding>?> CharacterAsync(QualityCall call, string sheet, string character, IReadOnlyList<QualityPicture> pictures, int scenes,
            CancellationToken cancellationToken)
        {
            CharacterCalls++;
            return Task.FromResult<IReadOnlyList<QualityFinding>?>(Character);
        }

        public Task<IReadOnlyList<CriticScore>> CriticAsync(QualityCall call, string sheet, RenderInput input, IReadOnlyList<QualityFrame> frames, bool subtitles,
            CancellationToken cancellationToken)
        {
            CriticCalls++;
            return Task.FromResult<IReadOnlyList<CriticScore>>([new CriticScore("Readable text", 5, "ok"), new CriticScore("Ending", 3, "abrupt")]);
        }

        public Task<PlanItem?> RepairAsync(QualityRepairRequest request, CancellationToken cancellationToken)
        {
            Repairs.Add(request.Scene);
            request.Findings.ShouldAllBe(f => f.Scene == request.Scene, "a repair gets only its own scene's findings");
            return Task.FromResult<PlanItem?>(new PlanItem
            {
                Scene = request.Scene,
                Plan = new RenderPlan { Base = PlanBases.Title, Title = "Fixed", Beats = [new PlanBeat { Sentence = 1 }] },
            });
        }
    }

    private sealed class FakeSheet : IPlanSheet
    {
        public List<string> Videos { get; } = [];

        public Task<bool> SheetAsync(string video, IReadOnlyList<double> seconds, int columns, string png, CancellationToken cancellationToken)
        {
            Videos.Add(video);
            Directory.CreateDirectory(Path.GetDirectoryName(png)!);
            File.WriteAllBytes(png, PlaceholderImage.Png(8, 8));
            return Task.FromResult(true);
        }
    }

    private static QualityFinding Blocker(int scene, string problem = "cut label") => new(scene, QualitySeverity.Blocker, problem);

    private static QualityFinding Polish(int scene, string problem = "soft edge") => new(scene, QualitySeverity.Polish, problem);

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
                new PlanBeat { Sentence = 2, Start = input.Scenes[1].StartSeconds + 3, Duration = 3 }],
        };
        return input;
    }

    private static ResolvedModel Model(string provider = "anthropic", string id = "claude-haiku-5-5") =>
        new(new EmbeddedModelCatalog().Find(provider, id).ShouldNotBeNull(), new ApiKey { Provider = provider, Label = "k", Secret = "fake-test-key-quality-0123" },
            string.Empty);

    private (RenderQualityJob Job, VideoPageTests.FakeEngine Engine) Job(QualityMode mode, SceneDrawer drawer, ResolvedModel? reviewer = null)
    {
        var work = Path.Combine(_folder, "work");
        var project = Path.Combine(work, "project");
        Directory.CreateDirectory(project);
        var input = Input();
        var output = Path.Combine(work, "silent.mp4");
        File.WriteAllBytes(output, new byte[16]);
        var engine = new VideoPageTests.FakeEngine();
        var options = new VideoOptions { Style = new VideoStyle { Drawer = drawer, Quality = mode } };
        var render = new RenderOutcome(new EngineRenderRequest(project, PlanDrawer.Code, input, output, 1), false, input);
        var report = new QualityReport { Mode = mode };
        var steps = new VideoStepProgress([(VideoSteps.Render, 1), (VideoSteps.Quality, 1), (VideoSteps.Mux, 1)], null);
        return (new RenderQualityJob(reviewer ?? Model(), Model("anthropic", "claude-sonnet-5-5"), engine, options, render, work, [null, null], ["a map", string.Empty],
            steps, Guid.NewGuid(), report), engine);
    }

    private static QualityStep Step(FakeReviewer reviewer, FakeSheet? sheet = null) => new(reviewer, sheet ?? new FakeSheet(), NullLogger<QualityStep>.Instance);

    [Fact]
    public async Task FastModeCallsNothingAndChangesNothing()
    {
        var reviewer = new FakeReviewer().Frames([Blocker(1)]);
        var sheet = new FakeSheet();
        var (job, engine) = Job(QualityMode.Fast, SceneDrawer.App);
        var outcome = await Step(reviewer, sheet).AfterRenderAsync(job, CancellationToken.None);

        outcome.ShouldBeSameAs(job.Render);
        reviewer.FrameCalls.ShouldBe(0);
        reviewer.CriticCalls.ShouldBe(0);
        sheet.Videos.ShouldBeEmpty("no sheet is made in Fast mode");
        engine.Renders.ShouldBe(0);
        job.Report.Frames.ShouldBeEmpty();
        QualityRules.Warnings(job.Report).ShouldBeEmpty();
        job.Steps.Spent.ShouldBeEmpty();
        new VideoStyle().Quality.ShouldBe(QualityMode.Fast, "a new template and one saved before read as Fast");
        QualityRules.Mode(null).ShouldBe(QualityMode.Fast);
    }

    [Fact]
    public async Task WithoutAReviewerNothingIsReviewed()
    {
        var reviewer = new FakeReviewer();
        var (job, _) = Job(QualityMode.Careful, SceneDrawer.App);
        var outcome = await Step(reviewer).AfterRenderAsync(job with { Reviewer = null }, CancellationToken.None);
        outcome.ShouldBeSameAs(job.Render);
        reviewer.FrameCalls.ShouldBe(0);
    }

    [Fact]
    public async Task StandardReviewsOnceScoresOnceAndNeverRepairs()
    {
        var reviewer = new FakeReviewer().Frames([Blocker(2), Polish(1)]);
        var (job, engine) = Job(QualityMode.Standard, SceneDrawer.App);
        var outcome = await Step(reviewer).AfterRenderAsync(job, CancellationToken.None);

        reviewer.FrameCalls.ShouldBe(1);
        reviewer.CriticCalls.ShouldBe(1);
        reviewer.Repairs.ShouldBeEmpty("Standard is a report only");
        engine.Renders.ShouldBe(0);
        outcome.Request.Output.ShouldBe(job.Render.Request.Output);
        job.Report.Frames.Count.ShouldBe(1);
        job.Report.FrameStop.ShouldBe(QualityStops.ReportOnly);
        job.Report.Critic.Count.ShouldBe(2);
        job.Report.Files.ShouldHaveSingleItem().ShouldEndWith("sheet-1.png");
        QualityRules.Warnings(job.Report).ShouldBe([VideoWarnings.QualityBlocker, VideoWarnings.QualityFails]);
    }

    [Fact]
    public async Task CarefulRepairsOnlyTheFaultyScenesAndRendersANewFile()
    {
        var reviewer = new FakeReviewer().Frames([Blocker(2), Blocker(2, "another")], []);
        var (job, engine) = Job(QualityMode.Careful, SceneDrawer.App);
        var outcome = await Step(reviewer).AfterRenderAsync(job, CancellationToken.None);

        reviewer.Repairs.ShouldBe([2], "one small plan call per faulty scene");
        engine.Renders.ShouldBe(1);
        outcome.Request.Output.ShouldEndWith("silent-q2.mp4");
        File.Exists(outcome.Request.Output).ShouldBeTrue();
        job.Render.Input.Scenes[1].Plan!.Title.ShouldBe("Fixed");
        job.Render.Input.Scenes[0].Plan!.Base.ShouldBe(PlanBases.Image, "a scene without a finding keeps its plan");
        job.Report.FrameStop.ShouldBe(QualityStops.Clean);
        job.Report.Frames.Select(r => r.Repaired.Count).ShouldBe([1, 0]);
        QualityRules.Warnings(job.Report).ShouldBe([VideoWarnings.QualityFails], "nothing is left; the critic's 3 still does not pass");
    }

    [Fact]
    public async Task CarefulInFreeCodeOnlyWarns()
    {
        var reviewer = new FakeReviewer().Frames([Blocker(1)]);
        var (job, engine) = Job(QualityMode.Careful, SceneDrawer.FreeCode);
        await Step(reviewer).AfterRenderAsync(job, CancellationToken.None);

        reviewer.Repairs.ShouldBeEmpty();
        engine.Renders.ShouldBe(0);
        job.Report.NotRepaired.ShouldBeTrue();
        job.Report.FrameStop.ShouldBe(QualityStops.NoRepair);
        QualityRules.Warnings(job.Report).ShouldContain(VideoWarnings.QualityNotRepaired);
    }

    [Fact]
    public async Task TheRoundsStopAtTheLimitAndNoRepairFollowsTheLastReview()
    {
        var reviews = 0;
        var repairs = 0;
        var (rounds, stop) = await QualityLoop.RunAsync((round, _) =>
        {
            reviews++;
            // Shrinks every round, never empty.
            return Task.FromResult<IReadOnlyList<QualityFinding>?>(Enumerable.Range(1, 4 - round).Select(s => Blocker(s)).ToList());
        }, (counted, _, _) =>
        {
            repairs++;
            return Task.FromResult(QualityRules.Scenes(counted));
        }, CancellationToken.None);

        reviews.ShouldBe(QualityLimits.MaxRounds);
        repairs.ShouldBe(QualityLimits.MaxRounds - 1);
        rounds.Count.ShouldBe(QualityLimits.MaxRounds);
        stop.ShouldBe(QualityStops.Limit);
        rounds[^1].Repaired.ShouldBeEmpty();
    }

    [Fact]
    public async Task AfterTheSecondRoundPolishDoesNotCount()
    {
        var (rounds, stop) = await QualityLoop.RunAsync((round, _) => Task.FromResult<IReadOnlyList<QualityFinding>?>(round switch
        {
            1 => [Blocker(1), Blocker(2), Polish(3)],
            2 => [Blocker(1), Polish(2)],
            _ => [Polish(1), Polish(2), Polish(3)],
        }), (counted, _, _) => Task.FromResult(QualityRules.Scenes(counted)), CancellationToken.None);

        stop.ShouldBe(QualityStops.Clean, "round 3 has only polish: nothing counts");
        rounds.Count.ShouldBe(3);
        rounds[2].Findings.ShouldBeEmpty();
        rounds[1].Findings.Count.ShouldBe(2, "round 2 still counts polish");
        QualityRules.Counted([Polish(1), Blocker(2)], QualityLimits.PolishRounds + 1).ShouldBe([Blocker(2)]);
    }

    [Fact]
    public async Task AListThatDoesNotShrinkStopsTheRounds()
    {
        var repairs = 0;
        var (rounds, stop) = await QualityLoop.RunAsync((round, _) => Task.FromResult<IReadOnlyList<QualityFinding>?>([Blocker(1), Blocker(2, "again " + round)]),
            (counted, _, _) =>
            {
                repairs++;
                return Task.FromResult(QualityRules.Scenes(counted));
            }, CancellationToken.None);

        stop.ShouldBe(QualityStops.Stuck);
        rounds.Count.ShouldBe(2);
        repairs.ShouldBe(1, "one repair, then the same count: no more paid repairs");
    }

    [Fact]
    public async Task AReviewThatCannotRunOrARepairThatChangesNothingEndsTheRounds()
    {
        (await QualityLoop.RunAsync((_, _) => Task.FromResult<IReadOnlyList<QualityFinding>?>(null), null, CancellationToken.None)).Stop
            .ShouldBe(QualityStops.Skipped);
        (await QualityLoop.RunAsync((_, _) => Task.FromResult<IReadOnlyList<QualityFinding>?>([Blocker(1)]),
            (_, _, _) => Task.FromResult<IReadOnlyList<int>>([]), CancellationToken.None)).Stop.ShouldBe(QualityStops.NoRepair);
        (await QualityLoop.RunAsync((_, _) => Task.FromResult<IReadOnlyList<QualityFinding>?>([Blocker(1)]), null, CancellationToken.None)).Stop
            .ShouldBe(QualityStops.ReportOnly);
    }

    /// <summary>A reviewer whose every call fails (a provider's error, an unreachable local server).</summary>
    private sealed class FailingReviewer : IQualityReviewer
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<QualityFinding>?> FramesAsync(QualityCall call, string sheet, RenderInput input, IReadOnlyList<QualityFrame> frames, bool subtitles,
            CancellationToken cancellationToken) => Fail<IReadOnlyList<QualityFinding>?>();

        public Task<IReadOnlyList<QualityFinding>?> PicturesAsync(QualityCall call, IReadOnlyList<QualityPicture> pictures, int scenes, CancellationToken cancellationToken) =>
            Fail<IReadOnlyList<QualityFinding>?>();

        public Task<IReadOnlyList<QualityFinding>?> CharacterAsync(QualityCall call, string sheet, string character, IReadOnlyList<QualityPicture> pictures, int scenes,
            CancellationToken cancellationToken) => Fail<IReadOnlyList<QualityFinding>?>();

        public Task<IReadOnlyList<CriticScore>> CriticAsync(QualityCall call, string sheet, RenderInput input, IReadOnlyList<QualityFrame> frames, bool subtitles,
            CancellationToken cancellationToken) => Fail<IReadOnlyList<CriticScore>>();

        public Task<PlanItem?> RepairAsync(QualityRepairRequest request, CancellationToken cancellationToken) => Fail<PlanItem?>();

        private Task<T> Fail<T>()
        {
            Calls++;
            throw new HttpRequestException("the reviewer cannot be reached");
        }
    }

    [Fact]
    public async Task AFailingReviewerNeverStopsTheVideo()
    {
        var (job, engine) = Job(QualityMode.Careful, SceneDrawer.App);
        var reviewer = new FailingReviewer();
        var outcome = await new QualityStep(reviewer, new FakeSheet(), NullLogger<QualityStep>.Instance).AfterRenderAsync(job, CancellationToken.None);

        outcome.ShouldBeSameAs(job.Render, "the rendered video is kept");
        reviewer.Calls.ShouldBe(2, "one frame review and the critic, both failed");
        engine.Renders.ShouldBe(0);
        job.Report.FrameStop.ShouldBe(QualityStops.Skipped);
        job.Report.Critic.ShouldBeEmpty();

        // A failed repair of a scene and a failed redraw of the pictures end the rounds instead of the video.
        var repairs = new FakeReviewer().Frames([Blocker(2)]);
        var (second, secondEngine) = Job(QualityMode.Careful, SceneDrawer.App);
        var failingRepair = new RepairFailingReviewer(repairs);
        (await new QualityStep(failingRepair, new FakeSheet(), NullLogger<QualityStep>.Instance).AfterRenderAsync(second, CancellationToken.None))
            .ShouldBeSameAs(second.Render);
        secondEngine.Renders.ShouldBe(0);
        second.Report.FrameStop.ShouldBe(QualityStops.NoRepair);

        var report = new QualityReport { Mode = QualityMode.Careful };
        var pictures = new PictureQualityJob(Model(), Loop: true, [new QualityPicture(1, "scene-1.png", "a map")], 1, string.Empty, null,
            (_, _) => throw new VideoException("the image model failed") { Code = "images" }, new VideoStepProgress([(VideoSteps.Images, 1)], null),
            Guid.NewGuid(), report);
        await Step(new FakeReviewer().Pictures([Blocker(1)])).PicturesAsync(pictures, CancellationToken.None);
        report.PictureStop.ShouldBe(QualityStops.NoRepair);
    }

    /// <summary>The fake reviewer, but every repair call fails.</summary>
    private sealed class RepairFailingReviewer(FakeReviewer inner) : IQualityReviewer
    {
        public Task<IReadOnlyList<QualityFinding>?> FramesAsync(QualityCall call, string sheet, RenderInput input, IReadOnlyList<QualityFrame> frames, bool subtitles,
            CancellationToken cancellationToken) => inner.FramesAsync(call, sheet, input, frames, subtitles, cancellationToken);

        public Task<IReadOnlyList<QualityFinding>?> PicturesAsync(QualityCall call, IReadOnlyList<QualityPicture> pictures, int scenes, CancellationToken cancellationToken) =>
            inner.PicturesAsync(call, pictures, scenes, cancellationToken);

        public Task<IReadOnlyList<QualityFinding>?> CharacterAsync(QualityCall call, string sheet, string character, IReadOnlyList<QualityPicture> pictures, int scenes,
            CancellationToken cancellationToken) => inner.CharacterAsync(call, sheet, character, pictures, scenes, cancellationToken);

        public Task<IReadOnlyList<CriticScore>> CriticAsync(QualityCall call, string sheet, RenderInput input, IReadOnlyList<QualityFrame> frames, bool subtitles,
            CancellationToken cancellationToken) => inner.CriticAsync(call, sheet, input, frames, subtitles, cancellationToken);

        public Task<PlanItem?> RepairAsync(QualityRepairRequest request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("the code model cannot be reached");
    }

    [Fact]
    public void ARecordedReviewIsReadTolerantly()
    {
        var findings = QualityPrompt.ReadFindings(RecordedReview, 2).ShouldNotBeNull();
        findings.Count.ShouldBe(4);
        findings[0].ShouldBe(new QualityFinding(1, QualitySeverity.Polish, "Title text is a little close to the top edge."));
        findings.Where(f => f.Scene == 2).Select(f => f.Severity).ShouldBe([QualitySeverity.Blocker, QualitySeverity.Blocker, QualitySeverity.Polish]);
        findings.ShouldNotContain(f => f.Scene == 7);
        findings.Count(f => f.Problem.StartsWith("The label", StringComparison.Ordinal)).ShouldBe(1, "a repeated problem is kept once");
        findings.ShouldNotContain(f => f.Problem.StartsWith("Fourth", StringComparison.Ordinal), "at most three per scene");

        QualityPrompt.ReadFindings("I could not see the image.", 2).ShouldBeNull("an unreadable review is skipped, not an empty pass");
        QualityPrompt.ReadFindings("{\"findings\":[]}", 2).ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public void ARecordedCriticIsReadInTheHeadingsOrder()
    {
        var scores = QualityPrompt.ReadCritic(RecordedCritic);
        scores.Select(s => s.Heading).ShouldBe(["Readable text", "Picture fits the narration", "Opening", "Ending"]);
        scores.Single(s => s.Heading == "Ending").Score.ShouldBe(QualityLimits.CriticMax, "held within 1 to 5; the first answer of a heading wins");
        scores.Single(s => s.Heading == "Picture fits the narration").Fails.ShouldBeTrue();
        scores.Single(s => s.Heading == "Opening").Fails.ShouldBeFalse("4 passes");
        QualityPrompt.ReadCritic("nothing").ShouldBeEmpty();
    }

    [Fact]
    public async Task TheReviewerSendsTheSheetAsAPictureAndPaysUnderItsOwnLine()
    {
        Directory.CreateDirectory(_folder);
        var sheet = Path.Combine(_folder, "sheet.png");
        File.WriteAllBytes(sheet, PlaceholderImage.Png(8, 8));
        var chat = new FakeChat()
            .Answer(_ => new ChatResponse(RecordedReview, new ChatUsage(2000, 0, 0, 300), null, "end_turn", "r1"))
            .Answer(_ => new ChatResponse(RecordedCritic, new ChatUsage(2000, 0, 0, 200), null, "end_turn", "r2"));
        var reviewer = new QualityReviewer(chat, new CostCalculator(), NullLogger<QualityReviewer>.Instance);
        var steps = new VideoStepProgress([(VideoSteps.Quality, 1)], null);
        var call = new QualityCall(Model(), steps, Guid.NewGuid());
        var input = Input();
        var frames = QualityRules.Moments(input);

        (await reviewer.FramesAsync(call, sheet, input, frames, true, CancellationToken.None)).ShouldNotBeNull().Count.ShouldBe(4);
        (await reviewer.CriticAsync(call, sheet, input, frames, true, CancellationToken.None)).Count.ShouldBe(4);

        chat.Requests.Count.ShouldBe(2);
        chat.Requests.ShouldAllBe(r => r.Images != null && r.Images.Count == 1);
        chat.Requests[0].User.ShouldContain("1. scene 1, start");
        chat.Requests[0].User.ShouldContain("subtitles on");
        steps.Spent.Select(c => c.Step).ShouldBe([PipelineSteps.QualityCheck, PipelineSteps.QualityCriticCost]);
        steps.Spent.ShouldAllBe(c => c.Usd > 0 && c.Exact);
    }

    [Fact]
    public void TheSheetHasEveryScenesStartMiddleEndAndItsBeats()
    {
        var input = Input();
        var frames = QualityRules.Moments(input);
        foreach (var scene in input.Scenes)
        {
            frames.Where(f => f.Scene == scene.Number).Select(f => f.Moment).ShouldContain(QualityRules.Start);
            frames.Where(f => f.Scene == scene.Number).Select(f => f.Moment).ShouldContain(QualityRules.Middle);
            frames.Where(f => f.Scene == scene.Number).Select(f => f.Moment).ShouldContain(QualityRules.End);
        }

        frames.ShouldContain(f => f.Moment == QualityRules.Beat && f.Scene == 2);
        frames.Select(f => f.Seconds).ShouldBeInOrder();
        frames.ShouldAllBe(f => f.Seconds >= 0 && f.Seconds <= input.DurationSeconds);
        var few = QualityRules.Moments(input, 4);
        few.Count.ShouldBeLessThanOrEqualTo(4);
        few.Select(f => f.Scene).Distinct().Count().ShouldBe(2, "thinned evenly, both scenes keep a frame");
    }

    [Fact]
    public async Task CarefulLocalPicturesAreMadeAgainAndTheCharacterJoinsTheFirstRound()
    {
        var reviewer = new FakeReviewer { Character = [Blocker(2, "other coat")] }.Pictures([Blocker(1, "garbled text")], []);
        var redrawn = new List<IReadOnlyList<int>>();
        var report = new QualityReport { Mode = QualityMode.Careful };
        var steps = new VideoStepProgress([(VideoSteps.Images, 1)], null);
        Directory.CreateDirectory(_folder);
        var sheet = Path.Combine(_folder, "character-sheet.png");
        var job = new PictureQualityJob(Model(), Loop: true,
            [new QualityPicture(1, "scene-1.png", "a map"), new QualityPicture(2, "scene-2.png", "a narrator")], 2, "a stick figure in a red coat",
            _ =>
            {
                File.WriteAllBytes(sheet, PlaceholderImage.Png(8, 8));
                return Task.FromResult<string?>(sheet);
            },
            (findings, _) =>
            {
                redrawn.Add(QualityRules.Scenes(findings));
                return Task.FromResult(QualityRules.Scenes(findings));
            }, steps, Guid.NewGuid(), report);

        await Step(reviewer).PicturesAsync(job, CancellationToken.None);

        reviewer.CharacterCalls.ShouldBe(1, "the sheet is compared once");
        reviewer.PictureCalls.ShouldBe(2);
        redrawn.ShouldHaveSingleItem().ShouldBe([1, 2], "the character's difference joins the first round");
        report.CharacterSheet.ShouldBeTrue();
        report.CharacterMismatch.ShouldBeEmpty("the picture was made again");
        report.PictureStop.ShouldBe(QualityStops.Clean);
        report.Files.ShouldContain(sheet);
    }

    [Fact]
    public async Task OnlineOrStandardPicturesAreNotMadeAgain()
    {
        var reviewer = new FakeReviewer { Character = [Blocker(2, "other coat")] };
        var report = new QualityReport { Mode = QualityMode.Careful };
        var redraws = 0;
        var job = new PictureQualityJob(Model(), Loop: false, [new QualityPicture(2, "scene-2.png", "x")], 2, "a narrator",
            _ => Task.FromResult<string?>(null), (_, _) =>
            {
                redraws++;
                return Task.FromResult<IReadOnlyList<int>>([]);
            }, new VideoStepProgress([(VideoSteps.Images, 1)], null), Guid.NewGuid(), report);

        await Step(reviewer).PicturesAsync(job, CancellationToken.None);
        reviewer.PictureCalls.ShouldBe(0);
        reviewer.CharacterCalls.ShouldBe(0, "no sheet was drawn, nothing to compare");
        redraws.ShouldBe(0);

        var standard = new QualityReport { Mode = QualityMode.Standard };
        await Step(reviewer).PicturesAsync(job with { Report = standard, Loop = true }, CancellationToken.None);
        reviewer.PictureCalls.ShouldBe(0, "only the careful mode looks at the pictures");
    }

    [Fact]
    public void FastAddsNothingToTheEstimate()
    {
        var (estimator, concept, profile, engine, code, image) = Estimating();
        var reviewer = new EmbeddedModelCatalog().Find("anthropic", "claude-haiku-5-5").ShouldNotBeNull();
        var fast = new VideoStyle { Drawer = SceneDrawer.App };
        estimator.Estimate([concept], profile, engine, code, image, null, true, fast, reviewer)
            .ShouldBe(estimator.Estimate([concept], profile, engine, code, image, null, true, fast));
        estimator.QualityLines([concept], profile, engine, code, image, reviewer, true, fast).ShouldBeEmpty();
        estimator.QualityLines([concept], profile, engine, code, image, null, true, new VideoStyle { Quality = QualityMode.Careful })
            .ShouldBeEmpty("without a reviewer nothing is paid");
    }

    [Fact]
    public void TheEstimateHasTheReviewerTheCriticAndTheWorstCaseOfTheRepairs()
    {
        var (estimator, concept, profile, engine, code, image) = Estimating();
        var catalog = new EmbeddedModelCatalog();
        var online = catalog.Find("anthropic", "claude-haiku-5-5").ShouldNotBeNull();
        var standardStyle = new VideoStyle { Drawer = SceneDrawer.App, Quality = QualityMode.Standard };
        var carefulStyle = new VideoStyle { Drawer = SceneDrawer.App, Quality = QualityMode.Careful };

        var standard = estimator.QualityLines([concept], profile, engine, code, image, online, true, standardStyle);
        standard.Select(l => l.Step).ShouldBe([PipelineSteps.QualityCheck, PipelineSteps.QualityCriticCost]);
        standard.ShouldAllBe(l => l.Cost.MinUsd > 0 && l.Cost.MaxUsd >= l.Cost.MinUsd);

        var careful = estimator.QualityLines([concept], profile, engine, code, image, online, true, carefulStyle);
        careful.Select(l => l.Step).ShouldBe([PipelineSteps.CharacterSheetCost, PipelineSteps.QualityCheck, PipelineSteps.QualityRepairCost,
            PipelineSteps.QualityCriticCost]);
        var check = careful.Single(l => l.Step == PipelineSteps.QualityCheck).Cost;
        check.MinUsd.ShouldBe(standard[0].Cost.MinUsd, "one review at the low end");
        check.MaxUsd.ShouldBeGreaterThan(standard[0].Cost.MaxUsd * (QualityLimits.MaxRounds - 1), "every round and the character comparison at the high end");
        var repair = careful.Single(l => l.Step == PipelineSteps.QualityRepairCost).Cost;
        repair.MinUsd.ShouldBe(0);
        repair.MaxUsd.ShouldBeGreaterThan(0);
        careful.Single(l => l.Step == PipelineSteps.CharacterSheetCost).Cost.MaxUsd.ShouldBe(image!.ImageUsd);

        var withLines = estimator.Estimate([concept], profile, engine, code, image, null, true, carefulStyle, online);
        var without = estimator.Estimate([concept], profile, engine, code, image, null, true, carefulStyle);
        withLines.MaxUsd.ShouldBe(without.MaxUsd + careful.Sum(l => l.Cost.MaxUsd));
        withLines.MinUsd.ShouldBe(without.MinUsd + careful.Sum(l => l.Cost.MinUsd));

        var freeCode = estimator.QualityLines([concept], profile, engine, code, image, online, true,
            new VideoStyle { Drawer = SceneDrawer.FreeCode, Quality = QualityMode.Careful });
        freeCode.ShouldNotContain(l => l.Step == PipelineSteps.QualityRepairCost, "free code is not repaired");

        var local = catalog.Find("app-ollama", "gemma3:4b");
        if (local is not null)
        {
            estimator.QualityLines([concept], profile, engine, code, image, local, true, standardStyle).ShouldAllBe(l => l.Cost.MaxUsd == 0,
                "a local reviewer costs nothing");
        }
    }

    private static (VideoCostEstimator Estimator, Concept Concept, Profile Profile, VideoPageTests.FakeEngine Engine, CatalogModel Code, CatalogModel? Image) Estimating()
    {
        var catalog = new EmbeddedModelCatalog();
        var profile = new Profile { Name = "P" };
        var concept = new Concept
        {
            ProfileId = profile.Id,
            Title = "Greek fire",
            Summary = "s",
            Content = "[on screen: a map]\nIn 672 the city was under siege.\n[on screen: ships]\nA strange fire burned on the water.",
            Language = "en",
        };
        return (new VideoCostEstimator(new CostCalculator(), catalog), concept, profile, new VideoPageTests.FakeEngine(),
            catalog.Find("anthropic", "claude-sonnet-5-5").ShouldNotBeNull(), catalog.Find("fal", "fal-ai/flux-2/flash").ShouldNotBeNull());
    }

    [Fact]
    public void TheCharacterIsAddedToEveryPromptOnce()
    {
        IReadOnlyList<string> prompts = ["a map of the city", "a narrator points at ships ", "burning water"];
        var once = ImagePromptBuilder.WithCharacter(prompts, "A stick figure in a red coat, round head, black lines.");
        once.Count.ShouldBe(3);
        foreach (var prompt in once)
        {
            prompt.Split(ImagePromptBuilder.CharacterLead).Length.ShouldBe(2, "exactly once");
            prompt.ShouldEndWith("A stick figure in a red coat, round head, black lines.");
        }

        ImagePromptBuilder.WithCharacter(once, "A stick figure in a red coat, round head, black lines").ShouldBe(once, "a kept prompt is not given it twice");
        ImagePromptBuilder.WithCharacter(prompts, "  ").ShouldBe(prompts, "no character, the prompts as they were");
    }

    [Fact]
    public void TheCharacterIsAskedOnlyWhenTheModeReviews()
    {
        ImagePromptBuilder.User("C", ["a"], DrawStyle.Auto).ShouldBe(ImagePromptBuilder.User("C", ["a"], DrawStyle.Auto, character: false));
        ImagePromptBuilder.User("C", ["a"]).ShouldNotContain("\"character\"");
        ImagePromptBuilder.User("C", ["a"], null, character: true).ShouldContain("\"character\"");
        ImagePromptBuilder.Schema().ToJsonString().ShouldNotContain("character");
        ImagePromptBuilder.Schema(false, true)["required"]!.ToJsonString().ShouldBe("[\"prompts\",\"character\"]");
        ImagePromptBuilder.Schema(true)["required"]!.ToJsonString().ShouldBe("[\"prompts\",\"style\"]", "the style schema is as before");
        ImagePromptBuilder.ReadCharacter("{\"prompts\":[],\"character\":\"  a red\\ncoat  \"}").ShouldBe("a red coat");
        ImagePromptBuilder.ReadCharacter("{\"prompts\":[]}").ShouldBeEmpty();
    }

    [Fact]
    public void AReviewingTemplateWithoutAReviewerIsAnOptionalGap()
    {
        var facts = new ChecklistFacts { Template = true, CodeModel = true, Voice = true, QualityWithoutReviewer = true };
        var gap = ProductionChecklist.Gaps(facts).ShouldHaveSingleItem();
        gap.Code.ShouldBe(ProductionGaps.QualityReviewer);
        gap.Kind.ShouldBe(GapKind.Optional);
        ProductionChecklist.Blocks([gap]).ShouldBeFalse();
        ProductionChecklist.Gaps(new ChecklistFacts { Template = true, CodeModel = true, Voice = true }).ShouldBeEmpty();
    }
}
