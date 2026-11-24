using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Hardware;
using TMConcept.Core.Keys;
using TMConcept.Core.Media;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;
using TMConcept.Providers.Catalog;
using TMConcept.Tests.Engines;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Core;

/// <summary>
/// Music made for the concept (0.1.46, C5) without a model, a network or a graphics card: the local model's label and warning by card
/// (fake hardware), the price of a piece and the estimate's music line, the prompt from the plan's hints, the step that checks the model
/// before anything is paid for, makes the piece after the narration (the local one under the card's lock), spends its price and records
/// its licence; a failure stops the video with "music" and the queue's night mode goes on with the next video; a new version keeps the piece.
/// </summary>
public sealed class MusicGenerationTests : IDisposable
{
    private const long GiB = 1024L * 1024 * 1024;

    private static readonly CatalogModel Local = new()
    {
        Provider = ModelProviders.Local, Id = "ace-step-1.5", Name = "ACE-Step 1.5", Capabilities = ModelCapability.Music, License = "mit", MinVideoMemoryGb = 4,
        MemoryGb = 16,
    };

    private static readonly CatalogModel Clip = new()
    {
        Provider = "google", Id = "lyria-3-clip-preview", Name = "Lyria 3 Clip", Capabilities = ModelCapability.Music, MusicUsdPerPiece = 0.04m, MusicSeconds = 30,
    };

    private static readonly CatalogModel Song = new()
    {
        Provider = "google", Id = "lyria-3.5", Name = "Lyria 3.5", Capabilities = ModelCapability.Music, MusicUsdPerPiece = 0.08m,
    };

    private static readonly CatalogModel Eleven = new()
    {
        Provider = "elevenlabs", Id = "music_v1", Name = "Eleven Music", Capabilities = ModelCapability.Music, MusicUsdPerMinute = 0.15m,
    };

    private readonly TempPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    [Fact]
    public void TheLocalModelIsMarkedNvidiaOnlyOnAnotherCardAndUnverifiedOnLinux()
    {
        var nvidia = new GpuReport([new GpuAdapter(GpuVendor.Nvidia, "RTX", 8 * GiB)], true, true);
        var amd = new GpuReport([new GpuAdapter(GpuVendor.Amd, "Radeon", 16 * GiB)], false, true);
        var intel = new GpuReport([new GpuAdapter(GpuVendor.Intel, "Arc", 8 * GiB)], false, true);

        LocalMusicSupport.Of(nvidia, linux: false).NvidiaOnly.ShouldBeFalse();
        LocalMusicSupport.Of(amd, linux: false).NvidiaOnly.ShouldBeTrue("an AMD card gets the label and the warning");
        LocalMusicSupport.Of(intel, linux: false).NvidiaOnly.ShouldBeTrue();
        LocalMusicSupport.Of(GpuReport.None, linux: false).NvidiaOnly.ShouldBeTrue("no card at all");
        LocalMusicSupport.Of(nvidia, linux: true).Linux.ShouldBeTrue("never tried on Linux");
        LocalMusicSupport.Of(nvidia, linux: false).Linux.ShouldBeFalse();
    }

    [Fact]
    public void APieceCostsItsPriceAndAShortPieceIsNeverMadeTwice()
    {
        MusicPricing.Seconds(Clip, 95).ShouldBe(30, "Lyria 3 Clip always makes 30 s; the bed loops it");
        MusicPricing.Usd(Clip, 30).ShouldBe(0.04m);
        MusicPricing.Exact(Clip).ShouldBeTrue();
        MusicPricing.Usd(Song, MusicPricing.Seconds(Song, 300)).ShouldBe(0.08m, "one song whatever the video's length");

        MusicPricing.Seconds(Eleven, 44.2).ShouldBe(45);
        MusicPricing.Usd(Eleven, 45).ShouldBe(0.1125m);
        MusicPricing.Exact(Eleven).ShouldBeFalse("a price per minute is worked out from the length");
        MusicPricing.Seconds(Eleven, 600).ShouldBe(MusicGenerationRules.MaxSeconds, "a long video loops the longest piece");
        MusicPricing.Usd(Eleven, MusicGenerationRules.MaxSeconds).ShouldBe(0.3m);
        MusicPricing.Seconds(Eleven, 2).ShouldBe(MusicGenerationRules.MinSeconds);

        MusicPricing.Usd(Local, 60).ShouldBe(0, "the local model costs nothing");
    }

    [Fact]
    public void TheEstimateCountsOnePieceOfTheTemplatesMusicModel()
    {
        var catalog = new EmbeddedModelCatalog();
        var estimator = new VideoCostEstimator(new CostCalculator(), catalog);
        var profile = new Profile { Name = "P" };
        var concept = new Concept { ProfileId = profile.Id, Title = "Greek fire", Summary = "s", Content = "The story of the siege of 672 in a few words.", Language = "en" };
        VideoStyle Style(MusicSource source, string generator) => new() { Name = "S", Music = new VideoMusic { Source = source, Generator = generator } };

        var none = estimator.Estimate([concept], profile, null, null, null, null, subtitles: true, Style(MusicSource.Off, string.Empty));
        var clip = estimator.Estimate([concept], profile, null, null, null, null, subtitles: true, Style(MusicSource.GenerateOnline, "google/lyria-3-clip-preview"));
        var eleven = estimator.Estimate([concept], profile, null, null, null, null, subtitles: true, Style(MusicSource.GenerateOnline, "elevenlabs/music_v1"));
        var local = estimator.Estimate([concept], profile, null, null, null, null, subtitles: true, Style(MusicSource.GenerateLocal, "local/ace-step-1.5"));

        none.MinUsd.ShouldBe(0);
        clip.MinUsd.ShouldBe(0.04m, "the catalog's price of one Lyria 3 Clip piece");
        clip.MaxUsd.ShouldBe(0.04m);
        eleven.MinUsd.ShouldBe(0.025m, "the shortest piece, 10 s at $0.15 a minute");
        local.MaxUsd.ShouldBe(0);
        new VideoCostEstimator(new CostCalculator()).Estimate([concept], profile, null, null, null, null, true, Style(MusicSource.GenerateOnline,
            "google/lyria-3-clip-preview")).MinUsd.ShouldBe(0, "without the catalog the music is not counted");
    }

    [Fact]
    public void TheCatalogKnowsTheMusicModelsWithTheirPrices()
    {
        var catalog = new EmbeddedModelCatalog();
        var music = catalog.All.Where(m => m.Capabilities.HasFlag(ModelCapability.Music)).ToDictionary(m => ModelBase.KeyOf(m));

        music.Keys.ShouldBe(["local/ace-step-1.5", "google/lyria-3-clip-preview", "google/lyria-3.5", "elevenlabs/music_v1"], ignoreOrder: true);
        music["google/lyria-3-clip-preview"].MusicUsdPerPiece.ShouldBe(0.04m);
        music["google/lyria-3-clip-preview"].MusicSeconds.ShouldBe(30);
        music["google/lyria-3.5"].MusicUsdPerPiece.ShouldBe(0.08m);
        music["elevenlabs/music_v1"].MusicUsdPerMinute.ShouldBe(0.15m);
        music["local/ace-step-1.5"].Requires.ShouldBe([TMConcept.Core.Components.ComponentCodes.MusicEnvironment, TMConcept.Core.Components.ComponentCodes.MusicModel]);
        TMConcept.Core.Components.ComponentCodes.MusicEnvironment.ShouldStartWith(TMConcept.Core.Components.ComponentCodes.PythonEnvironmentPrefix);
        VideoCostBreakdown.Order.ShouldContain(PipelineSteps.Music);
    }

    [Fact]
    public void ThePromptComesFromTheHintsAndAsksForNoWords()
    {
        MusicHint Hint(string mood, string style, string tempo) => new() { Mood = mood, Style = style, Tempo = tempo };
        var hints = new MusicHint?[] { Hint("dark", "orchestral", "slow"), null, Hint("tense", "orchestral", "slow"), Hint("dark", "made up words", "fast") };

        var prompt = MusicPromptBuilder.Build(hints, NarrationTempo.Natural, instrumental: true, 30);

        prompt.ShouldStartWith("dark orchestral background music");
        prompt.ShouldContain("slow tempo");
        prompt.ShouldContain("about 30 seconds");
        prompt.ShouldContain("no vocals");
        prompt.ShouldNotContain("made up", Case.Insensitive, "only the app's own tag words reach the model");
        MusicPromptBuilder.Bpm("slow").ShouldBe(72);

        var plain = MusicPromptBuilder.Build([], NarrationTempo.Tight, instrumental: true, 30);
        plain.ShouldStartWith(MusicPromptBuilder.DefaultMood + " " + MusicPromptBuilder.DefaultStyle);
        plain.ShouldContain("fast tempo", Case.Sensitive, "without hints the template's tempo decides");
    }

    [Fact]
    public async Task ALocalModelThatIsNotInstalledStopsTheVideoBeforeAnythingIsPaidFor()
    {
        var generator = new FakeGenerator(Local) { Installed = false };
        var step = Step(generator);
        var style = new VideoStyle { Name = "M", Music = new VideoMusic { Source = MusicSource.GenerateLocal, Generator = "local/ace-step-1.5" } };

        var error = await Should.ThrowAsync<VideoException>(() => step.PrepareAsync(style, null, Work(), CancellationToken.None));

        error.Code.ShouldBe("music");
        generator.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task AMissingOrMismatchedModelStopsTheVideo()
    {
        var step = Step(new FakeGenerator(Clip));
        var none = new VideoStyle { Name = "M", Music = new VideoMusic { Source = MusicSource.GenerateOnline } };
        var local = new VideoStyle { Name = "M", Music = new VideoMusic { Source = MusicSource.GenerateLocal, Generator = "google/lyria-3-clip-preview" } };

        (await Should.ThrowAsync<VideoException>(() => step.PrepareAsync(none, null, Work(), CancellationToken.None))).Code.ShouldBe("music");
        (await Should.ThrowAsync<VideoException>(() => step.PrepareAsync(local, null, Work(), CancellationToken.None))).Code.ShouldBe("music");
        VideoStyleRules.Problems(none).ShouldContain(VideoStyleProblem.MusicModelMissing);
    }

    [Fact]
    public async Task AnOnlinePieceIsMadeAfterTheNarrationFromTheHintsAndItsPriceAndLicenceAreRecorded()
    {
        var generator = new FakeGenerator(Clip);
        var step = Step(generator);
        var style = new VideoStyle { Name = "M", Music = new VideoMusic { Source = MusicSource.GenerateOnline, Generator = "google/lyria-3-clip-preview" } };
        var work = Work();

        var prepared = (await step.PrepareAsync(style, null, work, CancellationToken.None)).ShouldNotBeNull();
        prepared.Automatic.ShouldBeTrue("the scene plan writes its music hints");
        prepared.Generate.ShouldNotBeNull().Key.Secret.ShouldBe("key");
        generator.Requests.ShouldBeEmpty("nothing is made before the narration and the plan");

        var input = Input(out var clips);
        input.Scenes[0].Plan = new TMConcept.Core.Plans.RenderPlan { Music = new MusicHint { Mood = "epic", Style = "cinematic", Tempo = "fast" } };
        var spent = new List<(decimal Usd, bool Exact)>();

        var settled = (await step.SettleAsync(prepared, style, input, clips, work, align: true, CancellationToken.None, (u, e) => spent.Add((u, e))))
            .ShouldNotBeNull();

        var request = generator.Requests.ShouldHaveSingleItem();
        request.Prompt.ShouldStartWith("epic cinematic");
        request.Instrumental.ShouldBeTrue("the default: no words over the narration");
        request.Seconds.ShouldBe(30);
        spent.ShouldBe([(0.04m, true)]);
        settled.Generate.ShouldBeNull();
        settled.Entry.License.ShouldBe("generated: Lyria 3 Clip, google terms of use");
        settled.Entry.Artist.ShouldBe("Lyria 3 Clip");
        AssetLedger.NeedsAttribution(settled.Entry).ShouldBeFalse();
        File.Exists(settled.File).ShouldBeTrue();
        settled.File.ShouldStartWith(Path.Combine(work, VideoFolders.Music));
    }

    [Fact]
    public async Task TheLocalPieceIsMadeUnderTheGraphicsCardsLock()
    {
        var gpu = new ImageBackendTests.FakeCoordinator();
        var generator = new FakeGenerator(Local) { Gpu = gpu };
        var step = Step(generator, gpu);
        var style = new VideoStyle { Name = "M", Music = new VideoMusic { Source = MusicSource.GenerateLocal, Generator = "local/ace-step-1.5" } };
        var work = Work();
        var prepared = (await step.PrepareAsync(style, null, work, CancellationToken.None)).ShouldNotBeNull();
        var input = Input(out var clips);
        var spent = 0;

        var settled = (await step.SettleAsync(prepared, style, input, clips, work, align: true, CancellationToken.None, (_, _) => spent++)).ShouldNotBeNull();

        gpu.Leased.ShouldBe(["local/ace-step-1.5"]);
        generator.HeldDuring.ShouldBe([1], "the piece is made while the lease is held");
        gpu.Held.ShouldBe(0, "the lease is given back");
        spent.ShouldBe(0, "the local model costs nothing");
        settled.Entry.License.ShouldBe("generated: ACE-Step 1.5, MIT");
    }

    [Fact]
    public async Task AFailedPieceStopsTheVideoWithMusic()
    {
        var generator = new FakeGenerator(Eleven) { Fail = true };
        var step = Step(generator);
        var style = new VideoStyle { Name = "M", Music = new VideoMusic { Source = MusicSource.GenerateOnline, Generator = "elevenlabs/music_v1" } };
        var work = Work();
        var prepared = (await step.PrepareAsync(style, null, work, CancellationToken.None)).ShouldNotBeNull();
        var input = Input(out var clips);

        var error = await Should.ThrowAsync<VideoException>(() => step.SettleAsync(prepared, style, input, clips, work, align: true, CancellationToken.None));

        error.Code.ShouldBe("music");
        error.InnerException.ShouldBeOfType<MusicGenerationException>();
    }

    [Fact]
    public async Task InNightModeAFailedPieceLetsTheQueueGoOnWithTheNextVideo()
    {
        var generator = new FakeGenerator(Clip) { FailFirst = 1 };
        var step = Step(generator);
        var style = new VideoStyle { Name = "M", Music = new VideoMusic { Source = MusicSource.GenerateOnline, Generator = "google/lyria-3-clip-preview" } };
        var items = new Queue<string>(["video 1", "video 2"]);
        var failures = new List<string>();

        async Task<QueueItemOutcome> MakeAsync(string item, CancellationToken token)
        {
            // As the video page does: a VideoException is a failed video (shown), anything else is not expected here.
            try
            {
                var work = Work();
                var prepared = await step.PrepareAsync(style, null, work, token);
                var input = Input(out var clips);
                await step.SettleAsync(prepared, style, input, clips, work, align: true, token);
                return QueueItemOutcome.Made;
            }
            catch (VideoException ex)
            {
                failures.Add(item + ":" + ex.Code);
                return QueueItemOutcome.Failed;
            }
        }

        var clock = new CountingTimeProvider();
        var run = new VideoQueueRunner(clock, NullLogger<VideoQueueRunner>.Instance)
            .RunAsync(_ => Task.FromResult(items.Count > 0 ? items.Dequeue() : null), MakeAsync, () => true, CancellationToken.None);
        while (!run.IsCompleted)
        {
            clock.Advance(VideoQueueRunner.NightGap);
            await Task.Delay(2, TestContext.Current.CancellationToken);
        }

        var result = await run;
        failures.ShouldBe(["video 1:music"]);
        result.End.ShouldBe(QueueRunEnd.Done, "night mode goes on after the failure");
        result.Made.ShouldBe(1);
        result.Failed.ShouldBe(1);

        // Night mode off: the first failure stops the queue.
        generator.FailFirst = 1;
        items = new Queue<string>(["video 3", "video 4"]);
        var stopped = await new VideoQueueRunner(clock, NullLogger<VideoQueueRunner>.Instance)
            .RunAsync(_ => Task.FromResult(items.Count > 0 ? items.Dequeue() : null), MakeAsync, () => false, CancellationToken.None);
        stopped.End.ShouldBe(QueueRunEnd.Failed);
        items.Count.ShouldBe(1, "video 4 is not made");
    }

    [Fact]
    public async Task ANewVersionKeepsThePieceAndDoesNotMakeItAgain()
    {
        var generator = new FakeGenerator(Clip);
        var step = Step(generator);
        var style = new VideoStyle { Name = "M", Music = new VideoMusic { Source = MusicSource.GenerateOnline, Generator = "google/lyria-3-clip-preview" } };
        var version = Path.Combine(_paths.DataDirectory, "library", "v1");
        Directory.CreateDirectory(Path.Combine(version, VideoFolders.Music));
        await File.WriteAllBytesAsync(Path.Combine(version, VideoFolders.Music, "lyria.mp3"), [1, 2, 3], TestContext.Current.CancellationToken);
        var kept = VideoMusicStep.GeneratedEntry(Clip);
        kept.File = "lyria.mp3";
        await AssetLedger.WriteAsync(version, [kept], CancellationToken.None);
        var work = Work();

        var prepared = (await step.PrepareAsync(style, version, work, CancellationToken.None)).ShouldNotBeNull();
        var input = Input(out var clips);
        var spent = 0;
        var settled = (await step.SettleAsync(prepared, style, input, clips, work, align: true, CancellationToken.None, (_, _) => spent++)).ShouldNotBeNull();

        prepared.Generate.ShouldBeNull("the version's piece is used");
        prepared.Automatic.ShouldBeFalse();
        generator.Requests.ShouldBeEmpty("no new piece, nothing paid");
        spent.ShouldBe(0);
        settled.Entry.License.ShouldBe(kept.License, "the licence record goes with the piece");
    }

    private VideoMusicStep Step(FakeGenerator generator, IGpuCoordinator? gpu = null) =>
        new(new MinuteMedia(), new EmptyLibrary(), new UnreadableAudio(), NullLogger<VideoMusicStep>.Instance, new FakeGenerators(generator), gpu);

    private string Work()
    {
        var work = Path.Combine(_paths.WorkDirectory, "w-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        return work;
    }

    private static RenderInput Input(out IReadOnlyList<SpeechClip?> clips)
    {
        clips = [null, null];
        return RenderInputBuilder.Build("t", "en", [new RenderSceneSource("v", "c", string.Empty, 4000), new RenderSceneSource("v", "c", string.Empty, 4000)],
            1080, 1920, 30);
    }

    /// <summary>The music models of one fake generator, with a key for an online one.</summary>
    private sealed class FakeGenerators(FakeGenerator generator) : IMusicGenerators
    {
        public IReadOnlyList<CatalogModel> Models => [generator.Model];

        public CatalogModel? Find(string key) => key == ModelBase.KeyOf(generator.Model) ? generator.Model
            : key == ModelBase.KeyOf(Clip) ? Clip
            : null;

        public bool Ready(CatalogModel model) => generator.Ready(model);

        public Task<ResolvedModel> ResolveAsync(CatalogModel model, CancellationToken cancellationToken) =>
            Task.FromResult(new ResolvedModel(model, new ApiKey { Provider = model.Provider, Secret = "key" }, string.Empty));

        public IMusicGenerator? For(CatalogModel model) => generator.Handles(model) ? generator : null;
    }

    /// <summary>Writes a small file as the piece (no model, no network); counts the requests; fails when told to.</summary>
    private sealed class FakeGenerator(CatalogModel model) : IMusicGenerator
    {
        public CatalogModel Model => model;

        public bool Installed { get; init; } = true;

        public bool Fail { get; init; }

        public int FailFirst { get; set; }

        public ImageBackendTests.FakeCoordinator? Gpu { get; init; }

        public List<MusicGenerationRequest> Requests { get; } = [];

        public List<int> HeldDuring { get; } = [];

        public bool Handles(CatalogModel other) => ModelBase.KeyOf(other) == ModelBase.KeyOf(model);

        public bool Ready(CatalogModel other) => Handles(other) && Installed;

        public async Task<GeneratedMusic> GenerateAsync(MusicGenerationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Fail || FailFirst-- > 0)
            {
                throw new MusicGenerationException("the model ended badly") { Code = "process" };
            }

            HeldDuring.Add(Gpu?.Held ?? 0);
            var file = Path.Combine(request.OutputFolder, model.Provider == ModelProviders.Local ? "piece.wav" : "lyria.mp3");
            await File.WriteAllBytesAsync(file, [1, 2, 3, 4], cancellationToken);
            return new GeneratedMusic(file, MusicPricing.Usd(model, request.Seconds), MusicPricing.Exact(model));
        }
    }

    /// <summary>ffprobe that reads a minute from any file.</summary>
    private sealed class MinuteMedia : IMediaTools
    {
        public IReadOnlyList<string> RequiredComponents { get; } = [];

        public Task MuxAsync(string folder, string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, MuxOverlay overlay, IReadOnlyList<long> fadesMs,
            string output, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<long?> AudioDurationAsync(string file, CancellationToken cancellationToken) => Task.FromResult<long?>(60_000);

        public Task<bool> ThumbnailAsync(string video, string jpeg, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<FramePixels?>> SampleAsync(string file, IReadOnlyList<long> atMs, FrameRegion region, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>An ffmpeg that cannot read the piece for its beats (it plays as it is).</summary>
    private sealed class UnreadableAudio : IMusicAudio
    {
        public Task<float[]?> ReadMonoAsync(string file, int sampleRate, int maxSeconds, string work, CancellationToken cancellationToken) =>
            Task.FromResult<float[]?>(null);

        public Task<bool> RenderBedAsync(string file, MusicBed bed, string output, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    /// <summary>No ready library.</summary>
    private sealed class EmptyLibrary : IMusicLibrary
    {
        public IReadOnlyList<MusicTrack> Tracks => [];

        public string Folder => string.Empty;

        public string PickedFolder => string.Empty;

        public bool Ready => false;

        public string? FileOf(MusicTrack track) => null;
    }
}
