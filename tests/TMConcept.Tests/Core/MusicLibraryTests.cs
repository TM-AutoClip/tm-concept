using Shouldly;
using TMConcept.Core.Music;
using TMConcept.Core.Plans;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;

namespace TMConcept.Tests.Core;

/// <summary>
/// The ready music library (0.1.45, C4 + F7 + F8): the shipped catalog, the tag match with the nearest style, the music window of the plan's
/// hints, the beat grid of a synthetic click track, the entry point and the loop's bounds, the scene changes moved onto the beats, and the
/// attribution of a library track. Pure: no network, no ffmpeg.
/// </summary>
public sealed class MusicLibraryTests
{
    private const int Rate = MusicLibraryRules.SampleRate;

    private static MusicTrack Track(string id, string[] moods, string[] styles, int bpm) =>
        new() { Id = id, Title = id, Moods = [.. moods], Styles = [.. styles], Bpm = bpm, License = "CC BY 4.0" };

    /// <summary>A click track: a short noise burst on every beat from <paramref name="first"/> seconds (fixed seed).</summary>
    private static float[] Clicks(double bpm, double first, double seconds)
    {
        var samples = new float[(int)(seconds * Rate)];
        var random = new Random(7);
        for (var t = first; t < seconds; t += 60 / bpm)
        {
            var start = (int)(t * Rate);
            for (var i = 0; i < Rate / 100 && start + i < samples.Length; i++)
            {
                samples[start + i] = (float)((random.NextDouble() * 2) - 1) * 0.8f;
            }
        }

        return samples;
    }

    [Fact]
    public void TheShippedCatalogIsUsableAndEveryTrackIsCcByWithItsHash()
    {
        var catalog = MusicCatalog.Shipped;

        MusicCatalog.Problems(catalog).ShouldBeEmpty();
        catalog.Tracks.Count.ShouldBeGreaterThanOrEqualTo(10);
        catalog.Tracks.ShouldAllBe(t => t.Id.StartsWith(MusicLibraryRules.LibraryPrefix) && t.License == "CC BY 4.0" && t.Artist == "Kevin MacLeod");
        catalog.Tracks.ShouldAllBe(t => t.Attribution.Contains(t.Title) && t.Attribution.Contains("incompetech.com"));
        MusicTags.Styles.Where(s => s != "pop").ShouldAllBe(s => catalog.Tracks.Any(t => t.Styles.Contains(s)), "every style but pop has a track");
    }

    [Fact]
    public void ABrokenCatalogNamesItsProblems()
    {
        var file = new MusicCatalogFile
        {
            Schema = 1,
            Version = "../x",
            Tracks =
            [
                new MusicTrack { Id = "a", DownloadUrl = "http://example.org/a.mp3", Sha256 = "x", File = "a/b.mp3", License = "CC BY-NC 4.0", Moods = ["loud"] },
            ],
        };

        var problems = MusicCatalog.Problems(file);

        problems.ShouldContain("version: not a folder name");
        problems.ShouldContain(p => p.Contains("https://incompetech.com"));
        problems.ShouldContain(p => p.Contains("SHA-256"));
        problems.ShouldContain(p => p.Contains("not CC BY 4.0"));
        problems.ShouldContain(p => p.Contains("tag"));
        Should.Throw<InvalidDataException>(() => MusicCatalog.Parse("{\"schema\": 2}"));
    }

    [Fact]
    public void TheHintsChooseTheTrackWithTheirMoodStyleAndTempo()
    {
        IReadOnlyList<MusicTrack> tracks =
        [
            Track("a-calm", ["calm"], ["ambient"], 70), Track("b-epic", ["epic", "action"], ["orchestral"], 130), Track("c-mystery", ["mysterious"], ["world"], 86),
        ];
        MusicHint Hint(string mood, string style, string tempo) => new() { Mood = mood, Style = style, Tempo = tempo };

        var epic = MusicMatcher.Choose(tracks, [Hint("epic", "orchestral", "fast"), Hint("action", "orchestral", "fast"), null], NarrationTempo.Natural)
            .ShouldNotBeNull();
        epic.Track.Id.ShouldBe("b-epic");
        epic.Nearest.ShouldBeFalse();

        MusicMatcher.Choose(tracks, [Hint("mysterious", "world", "slow")], NarrationTempo.Natural)!.Track.Id.ShouldBe("c-mystery");

        // Without hints the narration tempo decides: relaxed = slow and calm.
        MusicMatcher.Choose(tracks, [], NarrationTempo.Relaxed)!.Track.Id.ShouldBe("a-calm");
        MusicMatcher.Choose([], [Hint("calm", "", "")], NarrationTempo.Natural).ShouldBeNull();
    }

    [Fact]
    public void NoTrackOfTheAskedStyleTakesTheNearestStyle()
    {
        IReadOnlyList<MusicTrack> tracks =
        [
            Track("a-acoustic", ["uplifting"], ["acoustic"], 100), Track("b-cinematic", ["uplifting"], ["cinematic"], 100), Track("c-world", ["uplifting"], ["world"], 100),
        ];

        // Orchestral: its nearest style is cinematic.
        var choice = MusicMatcher.Choose(tracks, [new MusicHint { Mood = "uplifting", Style = "orchestral", Tempo = "medium" }], NarrationTempo.Natural)
            .ShouldNotBeNull();
        choice.Track.Id.ShouldBe("b-cinematic");
        choice.Nearest.ShouldBeTrue();

        // Medieval: its nearest style is world (before acoustic).
        MusicMatcher.Choose(tracks, [new MusicHint { Style = "medieval" }], NarrationTempo.Natural)!.Track.Id.ShouldBe("c-world");
        MusicMatcher.TempoOf(89).ShouldBe(MusicTags.Slow);
        MusicMatcher.TempoOf(126).ShouldBe(MusicTags.Fast);
        MusicMatcher.TempoOf(110).ShouldBe(MusicTags.Medium);
    }

    [Fact]
    public void TheMusicWindowFollowsTheStartAndEndHints()
    {
        MusicWindows.Of([null, null, null], 3).ShouldBe(new MusicWindow(0, 2, false, false));
        MusicWindows.Of([null, new MusicHint { Start = true, Transition = "fade" }, new MusicHint { End = true, Transition = "fade" }, null], 4)
            .ShouldBe(new MusicWindow(1, 2, true, true));

        // An end before the start is not an end; an end on the last scene takes the template's fade-out.
        MusicWindows.Of([new MusicHint { End = true }, new MusicHint { Start = true }, null], 3).ShouldBe(new MusicWindow(1, 2, false, false));
        MusicWindows.Of([null, null, new MusicHint { End = true, Transition = "fade" }], 3).ShouldBe(new MusicWindow(0, 2, false, false));
    }

    [Theory]
    [InlineData(120, 0.25)]
    [InlineData(95, 0.4)]
    [InlineData(150, 0.1)]
    [InlineData(165, 0.05)]
    [InlineData(72, 0.4)]
    public void TheBeatGridOfAClickTrackHasItsTempoAndPhase(double bpm, double first)
    {
        var grid = BeatTracker.Analyze(Clicks(bpm, first, 30), Rate).ShouldNotBeNull();

        grid.Bpm.ShouldBe(bpm, 0.3);
        var beat = 60 / bpm;
        var off = Math.Abs(grid.FirstBeatSeconds - (first % beat));
        Math.Min(off, beat - off).ShouldBeLessThan(0.03, $"first beat {grid.FirstBeatSeconds:0.000} s");
    }

    [Fact]
    public void SilenceHasNoBeatGrid()
    {
        BeatTracker.Analyze(new float[Rate * 10], Rate).ShouldBeNull();
        BeatTracker.Analyze(new float[100], Rate).ShouldBeNull();
    }

    [Fact]
    public void TheEntryIsTheStrongestCleanOpeningOnABarLine()
    {
        // 10 s soft intro, then 50 s loud, then 5 s silence.
        var rms = Enumerable.Repeat(0.05, 20).Concat(Enumerable.Repeat(0.5, 100)).Concat(Enumerable.Repeat(0.0, 10)).ToList();
        var energy = new MusicEnergy(rms, 0.5);
        var grid = new BeatGrid(120, 0.0);

        MusicBedPlanner.TailEnd(energy).ShouldBe(60);
        var entry = MusicBedPlanner.Entry(energy, grid, 30);
        entry.ShouldBe(10, "the first bar of the loud part, not the soft intro");
        (entry / grid.BarSeconds % 1).ShouldBe(0, 1e-9);
    }

    [Fact]
    public void ALongTrackPlaysOnceAndAShortOneLoopsOnBarLinesWithACrossFade()
    {
        var grid = new BeatGrid(120, 0.1);

        var once = MusicBedPlanner.Plan(2.1, 40, 120, grid);
        once.Copies.ShouldBe(1);
        once.CrossfadeSeconds.ShouldBe(0);
        once.LoopEndSeconds.ShouldBe(42.1, 1e-9);

        var loop = MusicBedPlanner.Plan(2.1, 95, 30.5, grid);
        loop.Copies.ShouldBeGreaterThan(1);
        loop.TotalSeconds.ShouldBeGreaterThanOrEqualTo(95);
        loop.LoopEndSeconds.ShouldBeLessThanOrEqualTo(30.5, "the quiet tail is never looped");
        ((loop.LoopEndSeconds - loop.EntrySeconds) / grid.BarSeconds % 1).ShouldBe(0, 1e-6, "the loop is whole bars");
        loop.CrossfadeSeconds.ShouldBe(grid.BeatSeconds, 1e-9, "one beat");
        (loop.TotalSeconds - (loop.LoopEndSeconds - loop.EntrySeconds - loop.CrossfadeSeconds)).ShouldBeLessThan(95, "no copy more than needed");

        // Bounds: the cross-fade stays within its limits and a quarter of a short part; the copies have a ceiling.
        var slow = MusicBedPlanner.Plan(0, 30, 9, new BeatGrid(70, 0));
        slow.CrossfadeSeconds.ShouldBeInRange(MusicLibraryRules.MinCrossfadeSeconds, Math.Min(MusicLibraryRules.MaxCrossfadeSeconds, (slow.LoopEndSeconds - slow.EntrySeconds) / 4));
        MusicBedPlanner.Plan(0, 100_000, 9, null).Copies.ShouldBe(MusicLibraryRules.MaxCopies);
        Should.Throw<ArgumentOutOfRangeException>(() => MusicBedPlanner.Plan(10, 30, 10, grid));
    }

    [Fact]
    public void SceneChangesMoveOntoTheBeatsWithinTheTempoAndTheSilence()
    {
        // 30 fps, 120 bpm: a beat every 15 frames. Changes at 100, 220 and 330 frames.
        var grid = new BeatGrid(120, 0);
        int[] frames = [100, 120, 110, 90];

        var natural = BeatAligner.Align(frames, [20, 20, 20, 0], 30, 0, grid, BeatAligner.Tolerance(NarrationTempo.Natural), 0, 3);
        var starts = natural.Take(3).Select((_, i) => natural.Take(i + 1).Sum()).ToList();
        starts.ShouldAllBe(s => s % 15 == 0, string.Join(",", starts));
        natural.Sum().ShouldBe(frames.Sum() + (starts[^1] - 330), "the last scene keeps its length");

        // A change 5 frames after a beat moves back only into the silence after the last word; without it, and with the next beat too far,
        // it stays.
        BeatAligner.Align([95, 50], [20, 0], 30, 0, grid, 0.25, 0, 1).ShouldBe([90, 50]);
        BeatAligner.Align([95, 50], [0, 0], 30, 0, grid, 0.25, 0, 1).ShouldBe([95, 50]);

        // 90 bpm (a beat every 20 frames): tight allows 0.12 s = 3 frames, so a change 5 frames from a beat stays; natural moves it.
        var slow = new BeatGrid(90, 0);
        BeatAligner.Align([105, 50], [20, 0], 30, 0, slow, BeatAligner.Tolerance(NarrationTempo.Tight), 0, 1).ShouldBe([105, 50]);
        BeatAligner.Align([105, 50], [20, 0], 30, 0, slow, BeatAligner.Tolerance(NarrationTempo.Natural), 0, 1).ShouldBe([100, 50]);

        // Outside the music's scenes nothing moves.
        BeatAligner.Align(frames, [20, 20, 20, 0], 30, 0, grid, 0.25, 2, 3).Take(2).ShouldBe([100, 120]);
        BeatAligner.Tolerance(NarrationTempo.Tight).ShouldBeLessThan(BeatAligner.Tolerance(NarrationTempo.Natural));
        BeatAligner.Tolerance(NarrationTempo.Natural).ShouldBeLessThan(BeatAligner.Tolerance(NarrationTempo.Relaxed));
    }

    [Fact]
    public void LoudMusicShortensTheTransition()
    {
        var energy = new MusicEnergy([0.1, 0.1, 0.1, 0.5, 0.1, 0.1], 0.5);
        var factors = BeatAligner.TransitionFactors([0, 1.6, 0.6], energy, t => t);

        factors.ShouldBe([1, MusicLibraryRules.LoudTransitionShare, 1]);
    }

    [Fact]
    public void ALibraryTrackAsksForCreditAndACc0TrackDoesNot()
    {
        var track = MusicCatalog.Shipped.Tracks[0];
        var entry = VideoMusicStep.TrackEntry(track, "x.mp3");

        entry.License.ShouldBe("CC BY 4.0");
        entry.Artist.ShouldBe("Kevin MacLeod");
        entry.SourceUrl.ShouldStartWith("https://incompetech.com/");
        AssetLedger.NeedsAttribution(entry).ShouldBeTrue();
        AssetLedger.AttributionText([entry]).ShouldContain(track.Title);

        var shared = VideoMusicStep.TrackEntry(new MusicTrack { Title = "S", License = "CC BY-SA 4.0", Attribution = "\"S\" by A" }, "s.mp3");
        AssetLedger.NeedsAttribution(shared).ShouldBeTrue();
        var free = VideoMusicStep.TrackEntry(new MusicTrack { Title = "F", License = "CC0 1.0" }, "f.mp3");
        AssetLedger.NeedsAttribution(free).ShouldBeFalse();
        MusicLicenses.AsksCredit("CC0 1.0").ShouldBeFalse();
    }

    [Fact]
    public void TheSceneSaysItsMusicOnlyWithTheAppsWords()
    {
        var plans = PlanReader.Read("""
            {"scenes": [
              {"scene": 1, "base": "title", "music": {"mood": "Epic", "style": "orchestral", "tempo": "fast", "start": true, "end": false, "transition": "fade"}},
              {"scene": 2, "base": "title", "music": {"mood": "angry", "style": "dubstep", "tempo": "", "start": false, "end": true, "transition": "swoosh"}},
              {"scene": 3, "base": "title", "music": {"mood": "", "style": "", "tempo": "", "start": false, "end": false, "transition": ""}}
            ]}
            """);

        plans[0].Plan.Music.ShouldNotBeNull().Mood.ShouldBe("epic");
        plans[0].Plan.Music!.Transition.ShouldBe("fade");
        plans[1].Plan.Music.ShouldNotBeNull().Mood.ShouldBe(string.Empty);
        plans[1].Plan.Music!.Style.ShouldBe(string.Empty);
        plans[1].Plan.Music!.End.ShouldBeTrue();
        plans[2].Plan.Music.ShouldBeNull("nothing usable");

        PlanPromptBuilder.Schema().ToJsonString().ShouldNotContain("music", Case.Insensitive);
        PlanPromptBuilder.Schema(music: true).ToJsonString().ShouldContain("\"music\"");
        PlanPromptBuilder.System(MotionIntensity.Normal).ShouldNotContain("Music:");
        PlanPromptBuilder.System(MotionIntensity.Normal, music: true).ShouldContain("orchestral");
    }

    [Fact]
    public void ALibrarySourceIsOfferedAndAPickedOpenverseTrackNeedsItsFile()
    {
        MusicSources.Available.ShouldBe([MusicSource.Off, MusicSource.Library, MusicSource.OwnFile]);
        var style = new VideoStyle { Name = "Müzik", Music = new VideoMusic { Source = MusicSource.Library } };
        VideoStyleRules.Problems(style).ShouldBeEmpty("chosen by the concept");

        style.Music.Track = new MusicTrack { Id = MusicLibraryRules.OpenversePrefix + "x", Title = "x" };
        VideoStyleRules.Problems(style).ShouldBe([VideoStyleProblem.MusicFileMissing]);
        style.Music.Track.File = "C:/music/x.mp3";
        VideoStyleRules.Problems(style).ShouldBeEmpty();
        VideoStyleRules.Copy(style).Music.Track!.File.ShouldBe("C:/music/x.mp3", "the pick is kept in the template's JSON");
    }
}
