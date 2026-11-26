using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.Media;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Core.Render;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;
using TMConcept.Core.Tools;
using TMConcept.Core.Videos;
using TMConcept.Engines.Tools;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Engines;

/// <summary>
/// The ready music library on this computer (0.1.45, C4): nothing is there until its install (rule 18: one component, listed with every
/// track and its size); the install fetches each track pinned by its SHA-256 from an allowed host and writes the marker last; a template whose
/// concept chooses the track stops before anything is paid for while the library is missing, and once it is there the plan's hints choose the
/// track, which is recorded with its licence. The downloader and ffmpeg are fakes: nothing is downloaded.
/// </summary>
public sealed class MusicLibraryStoreTests : IDisposable
{
    private readonly TempPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    private MusicLibraryStore Store(FakeDownloader downloader) => new(_paths, downloader, NullLogger<MusicLibraryStore>.Instance);

    [Fact]
    public void TheLibraryIsNotThereUntilTheUserAgreedToItsDownload()
    {
        var store = Store(new FakeDownloader());
        var component = new MusicLibraryComponent(store);

        store.Ready.ShouldBeFalse();
        component.Code.ShouldBe(ComponentCodes.MusicLibrary);
        component.Ready.ShouldBeFalse();
        var downloads = component.Downloads();
        downloads.Count.ShouldBe(store.Tracks.Count);
        downloads.ShouldAllBe(d => d.Host == "incompetech.com" && d.ApproximateBytes > 0);
        store.FileOf(store.Tracks[0]).ShouldBeNull();
        store.Tracks.ShouldAllBe(t => ToolManifest.AllowedHosts.Contains(new Uri(t.DownloadUrl).Host));
    }

    [Fact]
    public async Task TheInstallFetchesEveryTrackPinnedByItsHashAndWritesTheMarkerLast()
    {
        var downloader = new FakeDownloader();
        var store = Store(downloader);

        await store.InstallAsync(null, CancellationToken.None);

        downloader.Asked.Count.ShouldBe(store.Tracks.Count);
        downloader.Asked.ShouldAllBe(a => a.Tool == ToolKind.MusicTrack && a.Sha256.Length == 64);
        store.Ready.ShouldBeTrue();
        store.FileOf(store.Tracks[0]).ShouldNotBeNull().ShouldStartWith(store.Folder);
        new MusicLibraryComponent(store).Downloads().ShouldBeEmpty();

        // A second install downloads nothing that is there.
        downloader.Asked.Clear();
        await store.InstallAsync(null, CancellationToken.None);
        downloader.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task AConceptChosenTrackStopsTheVideoWhileTheLibraryIsMissing()
    {
        var store = Store(new FakeDownloader());
        var step = Step(store);
        var style = new VideoStyle { Name = "M", Music = new VideoMusic { Source = MusicSource.Library } };

        var error = await Should.ThrowAsync<VideoException>(() => step.PrepareAsync(style, null, Work(), CancellationToken.None));

        error.Code.ShouldBe("music");
    }

    [Fact]
    public async Task ThePlansHintsChooseTheTrackAndItsLicenceIsRecorded()
    {
        var store = Store(new FakeDownloader());
        await store.InstallAsync(null, CancellationToken.None);
        var step = Step(store);
        var style = new VideoStyle { Name = "M", Music = new VideoMusic { Source = MusicSource.Library } };
        var work = Work();

        var prepared = (await step.PrepareAsync(style, null, work, CancellationToken.None)).ShouldNotBeNull();
        prepared.Automatic.ShouldBeTrue();

        var input = RenderInputBuilder.Build("t", "en", [new RenderSceneSource("v", "c", string.Empty, 4000), new RenderSceneSource("v", "c", string.Empty, 4000)],
            1080, 1920, 30);
        foreach (var scene in input.Scenes)
        {
            scene.Plan = new TMConcept.Core.Plans.RenderPlan { Music = new MusicHint { Mood = "mysterious", Style = "world", Tempo = "slow" } };
        }

        var settled = (await step.SettleAsync(prepared, style, input, [null, null], work, align: true, CancellationToken.None)).ShouldNotBeNull();

        settled.Automatic.ShouldBeFalse();
        var chosen = store.Tracks.Single(t => t.Title == settled.Entry.Name);
        chosen.Styles.ShouldContain("world");
        chosen.Moods.ShouldContain("mysterious");
        settled.Entry.License.ShouldBe("CC BY 4.0");
        settled.Entry.Attribution.ShouldContain(chosen.Title);
        AssetLedger.NeedsAttribution(settled.Entry).ShouldBeTrue();
        File.Exists(settled.File).ShouldBeTrue();
        settled.Placement.ShouldNotBeNull().BedFile.ShouldBeNull("the fake ffmpeg read nothing: the track plays as it is");
    }

    [Fact]
    public async Task ThePickedTracksBeatsMoveTheSceneChangesAndItsEntryAndLoopAreLaidOut()
    {
        var store = Store(new FakeDownloader());
        await store.InstallAsync(null, CancellationToken.None);
        var audio = new ClickAudio(120, 0.25, 60);
        var step = new VideoMusicStep(new LengthOnlyMedia(), store, audio, NullLogger<VideoMusicStep>.Instance, new NoMusicModels(),
            new MusicPieceRunner(new ImageBackendTests.FakeCoordinator(), NullLogger<MusicPieceRunner>.Instance));
        var style = new VideoStyle { Name = "M", Tempo = NarrationTempo.Natural, Music = new VideoMusic { Source = MusicSource.Library, Track = store.Tracks[0] } };
        var work = Work();
        var prepared = (await step.PrepareAsync(style, null, work, CancellationToken.None)).ShouldNotBeNull();
        prepared.Automatic.ShouldBeFalse("a picked track is copied before anything is paid for");

        // 4.37 s and 3.9 s: the changes at 4.37 s and 8.27 s are 0.13 s and 0.23 s before a beat (a beat every 0.5 s from the entry).
        var input = RenderInputBuilder.Build("t", "en", [new RenderSceneSource("v", "c", string.Empty, 4370), new RenderSceneSource("v", "c", string.Empty, 3900),
            new RenderSceneSource("v", "c", string.Empty, 3000)], 1080, 1920, 30);

        var settled = (await step.SettleAsync(prepared, style, input, [null, null, null], work, align: true, CancellationToken.None)).ShouldNotBeNull();

        var bed = audio.Bed.ShouldNotBeNull();
        var entry = bed.EntrySeconds;
        ((entry - 0.25) / 2 % 1).ShouldBe(0, 0.02, "the entry is a bar line of the grid");
        foreach (var scene in input.Scenes.Skip(1))
        {
            var fromEntry = scene.StartSeconds / 0.5;
            Math.Abs(fromEntry - Math.Round(fromEntry)).ShouldBeLessThan(0.1, $"scene {scene.Number} starts on a beat ({scene.StartSeconds} s)");
        }

        input.Scenes[1].StartSeconds.ShouldBe(4.5, 0.04);
        bed.Copies.ShouldBe(1, "a minute of music is longer than the video");
        bed.LengthSeconds.ShouldBe(input.DurationSeconds, 0.001);
        settled.Placement.ShouldNotBeNull().BedFile.ShouldNotBeNull().ShouldEndWith(VideoMusicStep.BedName);
    }

    private string Work()
    {
        var work = Path.Combine(_paths.WorkDirectory, "w-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        return work;
    }

    private static VideoMusicStep Step(MusicLibraryStore store) => new(new LengthOnlyMedia(), store, new SilentAudio(), NullLogger<VideoMusicStep>.Instance,
        new NoMusicModels(), new MusicPieceRunner(new ImageBackendTests.FakeCoordinator(), NullLogger<MusicPieceRunner>.Instance));

    /// <summary>Writes a file of the pinned size (no network).</summary>
    /// <summary>No music model at all: these tests use the library's tracks only.</summary>
    private sealed class NoMusicModels : IMusicGenerators
    {
        public IReadOnlyList<CatalogModel> Models => [];

        public CatalogModel? Find(string generator) => null;

        public bool Ready(CatalogModel model) => false;

        public Task<ResolvedModel> ResolveAsync(CatalogModel model, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No music model in these tests.");

        public IMusicGenerator? For(CatalogModel model) => null;
    }

    private sealed class FakeDownloader : IArchiveDownloader
    {
        public List<ToolArchive> Asked { get; } = [];

        public Task DownloadAsync(ToolArchive archive, string file, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
        {
            Asked.Add(archive);
            using var stream = File.Create(file);
            stream.SetLength(archive.ApproximateBytes);
            return Task.CompletedTask;
        }
    }

    /// <summary>ffprobe that reads a minute from any file.</summary>
    private sealed class LengthOnlyMedia : IMediaTools
    {
        public IReadOnlyList<string> RequiredComponents { get; } = [];

        public Task MuxAsync(string folder, string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, MuxOverlay overlay, IReadOnlyList<long> fadesMs,
            string output, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<long?> AudioDurationAsync(string file, CancellationToken cancellationToken) => Task.FromResult<long?>(60_000);

        public Task<bool> ThumbnailAsync(string video, string jpeg, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<FramePixels?>> SampleAsync(string file, IReadOnlyList<long> atMs, FrameRegion region, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>An ffmpeg that reads a click track from any file and keeps the bed it was asked to write.</summary>
    private sealed class ClickAudio(double bpm, double first, double seconds) : IMusicAudio
    {
        public MusicBed? Bed { get; private set; }

        public Task<float[]?> ReadMonoAsync(string file, int sampleRate, int maxSeconds, string work, CancellationToken cancellationToken)
        {
            var samples = new float[(int)(seconds * sampleRate)];
            var random = new Random(3);
            for (var t = first; t < seconds; t += 60 / bpm)
            {
                var start = (int)(t * sampleRate);
                for (var i = 0; i < sampleRate / 100 && start + i < samples.Length; i++)
                {
                    samples[start + i] = (float)((random.NextDouble() * 2) - 1) * 0.8f;
                }
            }

            return Task.FromResult<float[]?>(samples);
        }

        public Task<bool> RenderBedAsync(string file, MusicBed bed, string output, CancellationToken cancellationToken)
        {
            Bed = bed;
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllBytes(output, [1]);
            return Task.FromResult(true);
        }
    }

    /// <summary>An ffmpeg that cannot read the track for its beats.</summary>
    private sealed class SilentAudio : IMusicAudio
    {
        public Task<float[]?> ReadMonoAsync(string file, int sampleRate, int maxSeconds, string work, CancellationToken cancellationToken) =>
            Task.FromResult<float[]?>(null);

        public Task<bool> RenderBedAsync(string file, MusicBed bed, string output, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
