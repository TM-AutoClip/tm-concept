using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Music;
using TMConcept.Core.Plans;
using TMConcept.Core.References;
using TMConcept.Core.Scenes;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Core;

/// <summary>
/// The reference video measure (S1, 0.1.52; "bu videonun temposunda yap") on frame and sound sequences made in memory: cuts (a fast pan is
/// not one), the camera's way and share, the colour in CIELAB and its main colours, the beat of a click track, the tension's peaks (at most
/// three, at least 4 s apart), the profile → tempo / targets / ease / energy / palette, the emphasis moments by scene, the plan rule and the
/// plan request with a reference, and the measure's temporary folder removed after a success, a failure and a cancel (fake downloader and
/// reader). No ffmpeg, no yt-dlp, no network.
/// </summary>
public sealed class ReferenceVideoTests
{
    private const int Width = MotionCheckLimits.FrameWidth;
    private const int Height = MotionCheckLimits.FrameHeight;
    private const double Fps = ReferenceLimits.FrameFps;

    /// <summary>A textured grey picture (smooth waves and a fixed grain) seen with its content moved by (<paramref name="ox"/>, <paramref name="oy"/>).</summary>
    private static byte[] Texture(double ox, double oy, int brightness = 128, int seed = 1)
    {
        var frame = new byte[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var sx = x - ox;
                var sy = y - oy;
                var grain = (int)((((long)Math.Floor(sx) * 73856093) ^ ((long)Math.Floor(sy) * 19349663) ^ (seed * 83492791)) & 15);
                var value = brightness + (45 * Math.Sin(sx * 0.21)) + (35 * Math.Cos(sy * 0.27)) + (25 * Math.Sin((sx + sy) * 0.09)) + grain;
                frame[(y * Width) + x] = (byte)Math.Clamp(value, 0, 255);
            }
        }

        return frame;
    }

    private static ReferenceFrameSeries Read(IEnumerable<byte[]> frames)
    {
        var reader = new ReferenceFrameReader(Fps);
        foreach (var frame in frames)
        {
            reader.Add(frame);
        }

        return reader.Series();
    }

    /// <summary>Shots of <paramref name="shotFrames"/> frames that alternate between a dark and a bright still picture.</summary>
    private static IEnumerable<byte[]> AlternatingShots(int shots, int shotFrames)
    {
        var dark = Texture(0, 0, 55, 1);
        var bright = Texture(0, 0, 200, 2);
        for (var s = 0; s < shots; s++)
        {
            for (var f = 0; f < shotFrames; f++)
            {
                yield return s % 2 == 0 ? dark : bright;
            }
        }
    }

    [Fact]
    public void HardCutsAreCountedAndAFastPanIsNotACut()
    {
        // 6 shots of 2 s: 5 cuts in 12 s.
        var series = Read(AlternatingShots(6, 20));
        ReferenceCuts.Find(series).ShouldBe([20, 40, 60, 80, 100]);

        // A pan of 6 px a frame over the same picture: every frame changes a lot, the histogram does not.
        var pan = Read(Enumerable.Range(0, 40).Select(k => Texture(6.0 * k, 0)));
        pan.Change.Skip(1).Average().ShouldBeGreaterThan(0.05, "the pixels change");
        ReferenceCuts.Find(pan).ShouldBeEmpty();

        var profile = ReferenceProfileBuilder.Build(new ReferenceMeasureInput(series, null, null, 0), "a.mp4", false, DateTimeOffset.UnixEpoch);
        profile.Seconds.ShouldBe(12);
        profile.Cuts.ShouldBe(5);
        profile.CutsPer10Seconds.ShouldBe(4.17, 0.01);
        profile.AverageShotSeconds.ShouldBe(2);
        profile.HasSound.ShouldBeFalse();
    }

    [Theory]
    [InlineData(3, 0, CameraDirection.Left)]
    [InlineData(-3, 0, CameraDirection.Right)]
    [InlineData(0, 2, CameraDirection.Up)]
    [InlineData(0, -2, CameraDirection.Down)]
    public void TheCameraWayIsTheOppositeOfTheContentsMove(double dx, double dy, CameraDirection expected)
    {
        var series = Read(Enumerable.Range(0, 30).Select(k => Texture(dx * k, dy * k)));

        series.Moved.Skip(1).Count(m => m).ShouldBeGreaterThan(25, "every frame after the first moved with the camera");
        series.ShiftX[5].ShouldBe(-dx, 0.35);
        series.ShiftY[5].ShouldBe(-dy, 0.35);
        var camera = ReferenceCameraMeasure.Measure(series, ReferenceCuts.Find(series));
        camera.Direction.ShouldBe(expected);
        camera.Share.ShouldBe(1);
    }

    [Fact]
    public void AStillPictureHasNoCameraAndAShotShareCountsShots()
    {
        var still = Read(Enumerable.Repeat(Texture(0, 0), 30));
        still.Moved.ShouldAllBe(m => !m);
        ReferenceCameraMeasure.Measure(still, []).ShouldBe(new ReferenceCamera(0, CameraDirection.None, EaseShape.InOut));

        // Two shots of 3 s: the first still, the second panning, cut between them (another, darker picture).
        var frames = Enumerable.Repeat(Texture(0, 0, 200, 3), 30).Concat(Enumerable.Range(0, 30).Select(k => Texture(4.0 * k, 0, 60, 4)));
        var series = Read(frames);
        var cuts = ReferenceCuts.Find(series);
        cuts.ShouldBe([30]);
        var camera = ReferenceCameraMeasure.Measure(series, cuts);
        camera.Share.ShouldBe(0.5);
        camera.Direction.ShouldBe(CameraDirection.Left);
    }

    [Fact]
    public void AMoveThatSlowsDownEasesOut()
    {
        // Content shift per frame 7, 7, 6, … falling to 1: fast at the start, slow at the end.
        var offsets = new List<double> { 0 };
        foreach (var step in new double[] { 7, 7, 6, 6, 5, 5, 4, 4, 3, 3, 2, 2, 1.5, 1.5, 1.2, 1.2 })
        {
            offsets.Add(offsets[^1] + step);
        }

        var series = Read(offsets.Select(o => Texture(o, 0)));
        ReferenceCameraMeasure.Measure(series, []).Ease.ShouldBe(EaseShape.Out);

        offsets.Reverse();
        var growing = Read(offsets.Select(o => Texture(-o, 0)));
        ReferenceCameraMeasure.Measure(growing, []).Ease.ShouldBe(EaseShape.In);
    }

    [Fact]
    public void ShiftFindsAFractionAndPrefersStandingStillOnATie()
    {
        var previous = Enumerable.Range(0, 160).Select(i => Math.Sin(i * 0.2) * 50).ToArray();
        var current = Enumerable.Range(0, 160).Select(i => Math.Sin((i - 2.5) * 0.2) * 50).ToArray();
        ReferenceFrameReader.Shift(previous, current).ShouldBe(2.5, 0.2);

        var flat = new double[160];
        ReferenceFrameReader.Shift(flat, flat).ShouldBe(0);
        ReferenceFrameReader.Correlation([1, 2, 3, 4], [2, 4, 6, 8]).ShouldBe(1, 1e-9);
        ReferenceFrameReader.Correlation([1, 2, 3, 4], [4, 3, 2, 1]).ShouldBe(-1, 1e-9);
        ReferenceFrameReader.Correlation([5, 5, 5], [5, 5, 5]).ShouldBe(1);
    }

    [Fact]
    public void TheColourIsMeasuredInLabAndItsMainColoursAreFound()
    {
        var lab = LabColor.FromRgb(255, 0, 0);
        lab.L.ShouldBe(53.24, 0.05);
        lab.A.ShouldBe(80.09, 0.05);
        lab.B.ShouldBe(67.20, 0.05);
        LabColor.ToHex(lab.L, lab.A, lab.B).ShouldBe("#FF0000");
        LabColor.ToHex(100, 0, 0).ShouldBe("#FFFFFF");
        LabColor.ToHex(0, 0, 0).ShouldBe("#000000");

        // 3 red frames and 1 blue one.
        var reader = new ReferenceColorReader();
        byte[] Solid(byte r, byte g, byte b) => [.. Enumerable.Range(0, ReferenceColorReader.FrameBytes / 3).SelectMany(_ => new[] { r, g, b })];
        reader.Add(Solid(200, 30, 30));
        reader.Add(Solid(200, 30, 30));
        reader.Add(Solid(200, 30, 30));
        reader.Add(Solid(20, 40, 180));
        var colors = reader.Result().ShouldNotBeNull();

        colors.Palette.Count.ShouldBe(2, "the clusters of one colour are the same colour");
        colors.Palette[0].ShouldBe("#C81E1E", "the red covers most of the picture");
        colors.Palette[1].ShouldBe("#1428B4");
        colors.Saturation.ShouldBe(((3 * (170 / 200.0)) + (160 / 180.0)) / 4, 1e-6);
        colors.A.ShouldBeGreaterThan(20, "mostly red");
        new ReferenceColorReader().Result().ShouldBeNull();
    }

    [Fact]
    public void TheSoundGivesTheBeatAndASilentTrackIsNoSound()
    {
        const int rate = MusicLibraryRules.SampleRate;
        var seconds = 20;
        var samples = new float[seconds * rate];
        var random = new Random(7);
        for (var t = 0.25; t < seconds; t += 0.5)
        {
            var start = (int)(t * rate);
            for (var i = 0; i < rate / 100 && start + i < samples.Length; i++)
            {
                samples[start + i] = (float)((random.NextDouble() * 2) - 1) * 0.8f;
            }
        }

        var frames = Read(Enumerable.Repeat(Texture(0, 0), (int)(seconds * Fps)));
        var profile = ReferenceProfileBuilder.Build(new ReferenceMeasureInput(frames, null, samples, rate), "clicks.wav", false, DateTimeOffset.UnixEpoch);
        profile.HasSound.ShouldBeTrue();
        profile.Bpm.ShouldBe(120, 1.5);

        var silent = ReferenceProfileBuilder.Build(new ReferenceMeasureInput(frames, null, new float[seconds * rate], rate), "quiet.mp4", false,
            DateTimeOffset.UnixEpoch);
        silent.HasSound.ShouldBeFalse();
        silent.Bpm.ShouldBe(0);

        LoudnessCurve.Rms([1f, 1f, 0f, 0f], 4, 0.5).ShouldBe([0, -120], 0.01);
    }

    [Fact]
    public void PeaksAreTheThreeStrongestAtLeastFourSecondsApart()
    {
        // 40 s at 10 a second: bumps at 5 s (0.6), 6.5 s (0.9: wins over the one at 5 s), 15 s (0.5), 25 s (0.8), 35 s (0.7).
        var t = new double[400];
        void Bump(double at, double height)
        {
            for (var i = 0; i < t.Length; i++)
            {
                t[i] = Math.Max(t[i], height * Math.Exp(-Math.Pow((i / Fps) - at, 2) / 0.5));
            }
        }

        Bump(5, 0.6);
        Bump(6.5, 0.9);
        Bump(15, 0.5);
        Bump(25, 0.8);
        Bump(35, 0.7);

        var peaks = TensionCurve.Peaks(t, Fps);

        peaks.ShouldBe([6.5, 25, 35], 0.15);
        TensionCurve.Peaks(new double[400], Fps).ShouldBeEmpty("a flat film has no peak");
        TensionCurve.Robust01([0, 0.001, 0.002], 1).Max().ShouldBeLessThan(0.01, "a change below what a viewer notices is not stretched");
    }

    [Fact]
    public void TheTensionRisesWithLoudnessAndCuts()
    {
        var count = 300;
        var loud = Enumerable.Range(0, count).Select(i => i is >= 150 and < 170 ? -6.0 : -30.0).ToArray();
        var tension = TensionCurve.Compute(count, Fps, loud, new double[count], new double[count], [155, 158, 161, 164]);

        Array.IndexOf(tension, tension.Max()).ShouldBeInRange(150, 170);
        TensionCurve.Peaks(tension, Fps).Single().ShouldBeInRange(14, 16.5);
    }

    [Theory]
    [InlineData(6.5, NarrationTempo.Tight)]
    [InlineData(4, NarrationTempo.Tight)]
    [InlineData(3, NarrationTempo.Natural)]
    [InlineData(2.3, NarrationTempo.Natural)]
    [InlineData(1.8, NarrationTempo.Relaxed)]
    [InlineData(0, NarrationTempo.Relaxed)]
    public void TheNearestTempoIsChosenOnALogScale(double cuts, NarrationTempo expected) =>
        ReferenceTempoMapper.NearestTempo(cuts).ShouldBe(expected);

    [Fact]
    public void AProfileBecomesTheTemplatesTempoTargetsEaseEnergyAndPalette()
    {
        var profile = new ReferenceTempoProfile
        {
            CutsPer10Seconds = 9.3, CameraShare = 0.97, Ease = EaseShape.Out, LabL = 72, Palette = ["#808080", "#D03020", "#203040"], Peaks = [0.2, 0.8],
        };

        var suggestion = ReferenceTempoMapper.Suggest(profile);

        suggestion.Tempo.ShouldBe(NarrationTempo.Tight);
        suggestion.Energy.ShouldBe(TransitionEnergy.High);
        suggestion.Target.BeatsPer10Seconds.ShouldBe(ReferenceTempoMapper.MaxChanges, "kept within what a plan can do");
        suggestion.Target.CameraShare.ShouldBe(ReferenceTempoMapper.MaxCameraShare);
        suggestion.Target.Eases[0].ShouldBe("expo.out");
        suggestion.Target.Eases.ShouldBeUnique();
        suggestion.Target.Eases.ShouldAllBe(e => PlanEases.All.Contains(e));
        suggestion.Target.Checked.ShouldBeTrue();
        var palette = suggestion.Palette.ShouldNotBeNull();
        palette.Id.ShouldBe(VideoPalettes.CustomId);
        palette.Light.ShouldBeTrue("a bright reference");
        PaletteDesigner.Check(palette).Errors.ShouldBeEmpty();

        ReferenceTempoMapper.Suggest(new ReferenceTempoProfile()).Palette.ShouldBeNull("no colour measured");
        foreach (var shape in Enum.GetValues<EaseShape>())
        {
            foreach (var tempo in Enum.GetValues<NarrationTempo>())
            {
                PlanEases.All.ShouldContain(ReferenceTempoMapper.Ease(shape, tempo));
            }
        }

        ReferenceTempoMapper.Target(new VideoStyle()).ShouldBeNull("a template without a reference keeps the tempo's targets");
        var style = new VideoStyle { Tempo = NarrationTempo.Relaxed, Reference = profile };
        ReferenceTempoMapper.Target(style).ShouldNotBeNull().Eases[0].ShouldBe("sine.out", "the eases follow the template's own tempo");
    }

    [Fact]
    public void ThePeaksBecomeEmphasisMomentsByScene()
    {
        var profile = new ReferenceTempoProfile { Peaks = [0.1, 0.12, 0.55, 0.95] };
        var moments = ReferenceTempoMapper.Moments(profile, [10, 10, 10, 10]);
        moments.Select(m => m.Scene).ShouldBe([1, 3, 4], "two peaks in scene 1 are one moment");
        ReferenceTempoMapper.Moments(null, [10]).ShouldBeEmpty();

        var style = new VideoStyle { Reference = new ReferenceTempoProfile { Peaks = [0.55], CameraDirection = CameraDirection.Left, CutsPer10Seconds = 5 } };
        var scenes = new[] { Scene(1, 10), Scene(2, 10) };
        var user = PlanPromptBuilder.User("T", "en", style, scenes);
        user.ShouldContain("Reference video:");
        user.ShouldContain("scene 2 (at 55% of the video)");
        user.ShouldContain("mostly moves left");
        PlanPromptBuilder.User("T", "en", new VideoStyle(), scenes).ShouldNotContain("Reference video");
        PlanPromptBuilder.User("T", "en", style, [Scene(5, 10)]).Contains("Emphasis", StringComparison.Ordinal)
            .ShouldBeFalse("a single scene asked again has no place in the whole");

        var system = PlanPromptBuilder.System(MotionIntensity.Normal, NarrationTempo.Natural, [], false, ReferenceTempoMapper.Target(style));
        system.ShouldContain("measured from a reference video");
        system.ShouldContain("about 5 times every 10 seconds");
        PlanPromptBuilder.System(MotionIntensity.Normal).ShouldNotContain("reference");
    }

    [Fact]
    public void ThePlanRuleHoldsAPlanToTheReferenceBothWays()
    {
        // 30 s, natural tempo: without a reference nothing is checked.
        var scenes = Enumerable.Range(1, 3).Select(n => new PlanScene(n, 10, "v", [.. Enumerable.Range(0, 6).Select(i => "Sentence " + i)], string.Empty)).ToList();
        var few = Enumerable.Range(1, 3).Select(n => Item(n, beats: 1)).ToList();
        var check = new PlanCheck(scenes, [false, false, false], true);
        new TempoTargetRule().Problems(few, check).ShouldBeEmpty();

        var fast = new TempoTarget(5, 0.6, ["power2.inOut"], Checked: true);
        var findings = new TempoTargetRule().Problems(few, check with { Target = fast });
        findings.ShouldContain(f => f.Contains("reference video's tempo", StringComparison.Ordinal) && f.Contains("about 15 beats", StringComparison.Ordinal));

        var slow = new TempoTarget(1, 0.2, ["sine.inOut"], Checked: true);
        var many = Enumerable.Range(1, 3).Select(n => Item(n, beats: 6)).ToList();
        new TempoTargetRule().Problems(many, check with { Target = slow }).ShouldContain(f => f.Contains("leave more sentences without a beat", StringComparison.Ordinal));
        new TempoTargetRule().Problems(many, check with { Target = slow, Tempo = NarrationTempo.Relaxed, Motion = MotionIntensity.Lively })
            .ShouldBeEmpty("a lively motion still wins over a relaxed tempo");
    }

    [Fact]
    public async Task TheMeasuresFolderIsDeletedAfterASuccessAFailureAndACancel()
    {
        using var paths = new TempPaths();
        var downloader = new FakeDownloader();
        var reader = new FakeReader(Read(AlternatingShots(4, 30)));
        var measurer = new ReferenceMeasurer(paths, reader, new FakeAudio(), downloader, TimeProvider.System, NullLogger<ReferenceMeasurer>.Instance);
        var root = Path.Combine(paths.WorkDirectory, ReferenceLimits.Folder);
        var ct = TestContext.Current.CancellationToken;

        var stages = new List<string>();
        var profile = await measurer.MeasureAddressAsync("  https://example.com/watch?v=abc  ", new SyncProgress(p => stages.Add(p.Stage)), ct);
        profile.FromUrl.ShouldBeTrue();
        profile.Source.ShouldBe("https://example.com/watch?v=abc");
        profile.Cuts.ShouldBe(3);
        stages.ShouldContain(ReferenceStages.Download);
        stages.ShouldContain(ReferenceStages.Sound);
        downloader.Downloaded.ShouldNotBeNull();
        File.Exists(downloader.Downloaded).ShouldBeFalse("the downloaded video is not kept");
        Directory.EnumerateFileSystemEntries(root).ShouldBeEmpty();

        reader.Fail = true;
        (await Should.ThrowAsync<ReferenceMeasureException>(() => measurer.MeasureAddressAsync("https://example.com/v", null, ct))).Code.ShouldBe("unreadable");
        Directory.EnumerateFileSystemEntries(root).ShouldBeEmpty("a failure deletes the download too");

        reader.Fail = false;
        downloader.Cancel = true;
        await Should.ThrowAsync<OperationCanceledException>(() => measurer.MeasureAddressAsync("https://example.com/v", null, ct));
        Directory.EnumerateFileSystemEntries(root).ShouldBeEmpty("a cancel deletes the part that was downloaded");

        (await Should.ThrowAsync<ReferenceMeasureException>(() => measurer.MeasureAddressAsync("ftp://example.com/v", null, ct))).Code.ShouldBe("address");
        (await Should.ThrowAsync<ReferenceMeasureException>(() => measurer.MeasureAddressAsync("https://user:pass@example.com/v", null, ct))).Code
            .ShouldBe("address", "no sign-in in an address");
        downloader.Installed = false;
        (await Should.ThrowAsync<ReferenceMeasureException>(() => measurer.MeasureAddressAsync("https://example.com/v", null, ct))).Code.ShouldBe("missing");

        // A file of the user: only its name goes into the profile, and the file itself stays.
        var file = Path.Combine(paths.DataDirectory, "my clip.mp4");
        await File.WriteAllTextAsync(file, "video", ct);
        var own = await measurer.MeasureFileAsync(file, null, ct);
        own.Source.ShouldBe("my clip.mp4");
        own.FromUrl.ShouldBeFalse();
        File.Exists(file).ShouldBeTrue();
        Directory.EnumerateFileSystemEntries(root).ShouldBeEmpty();
        reader.Short = true;
        (await Should.ThrowAsync<ReferenceMeasureException>(() => measurer.MeasureFileAsync(file, null, ct))).Code.ShouldBe("short");
        (await Should.ThrowAsync<ReferenceMeasureException>(() => measurer.MeasureFileAsync(file + ".gone", null, ct))).Code.ShouldBe("file");
    }

    [Fact]
    public void AProfileIsStoredInTheTemplatesJsonAndATemplateWithoutOneReadsAsNone()
    {
        var style = new VideoStyle { Reference = new ReferenceTempoProfile { Source = "a.mp4", CutsPer10Seconds = 4.2, Palette = ["#112233"], Peaks = [0.5] } };
        var copy = VideoStyleRules.Copy(style);
        copy.Reference.ShouldNotBeNull().CutsPer10Seconds.ShouldBe(4.2);
        copy.Reference.Palette.ShouldBe(["#112233"]);
        var json = System.Text.Json.JsonSerializer.Serialize(new VideoStyle(), TMConcept.Core.Render.CoreJsonContext.Default.VideoStyle);
        System.Text.Json.JsonSerializer.Deserialize(json.Replace(",\"reference\":null", string.Empty, StringComparison.Ordinal),
            TMConcept.Core.Render.CoreJsonContext.Default.VideoStyle)!.Reference.ShouldBeNull();
    }

    private static PlanScene Scene(int number, double seconds) => new(number, seconds, "v", ["One.", "Two."], string.Empty);

    private static PlanItem Item(int scene, int beats) => new()
    {
        Scene = scene,
        Plan = new RenderPlan
        {
            Base = PlanBases.Title,
            Title = "T",
            Beats = [.. Enumerable.Range(0, beats).Select(i => new PlanBeat { Sentence = i, Camera = new PlanCamera { Zoom = 1.2 } })],
        },
    };

    private sealed class SyncProgress(Action<ReferenceProgress> report) : IProgress<ReferenceProgress>
    {
        public void Report(ReferenceProgress value) => report(value);
    }

    private sealed class FakeDownloader : IReferenceDownloader
    {
        public bool Installed { get; set; } = true;

        public bool Cancel { get; set; }

        public string? Downloaded { get; private set; }

        public bool Ready => Installed;

        public async Task<string> DownloadAsync(Uri address, string folder, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            Directory.Exists(folder).ShouldBeTrue();
            Directory.EnumerateFileSystemEntries(folder).ShouldBeEmpty();
            Downloaded = Path.Combine(folder, "reference.mkv");
            await File.WriteAllTextAsync(Downloaded, "video", cancellationToken);
            progress?.Report(0.5);
            if (Cancel)
            {
                await File.WriteAllTextAsync(Downloaded + ".part", "half", cancellationToken);
                throw new OperationCanceledException();
            }

            return Downloaded;
        }
    }

    private sealed class FakeReader(ReferenceFrameSeries series) : IReferenceVideoReader
    {
        public bool Fail { get; set; }

        public bool Short { get; set; }

        public Task<ReferenceFrameSeries?> ReadFramesAsync(string video, string work, CancellationToken cancellationToken)
        {
            File.Exists(video).ShouldBeTrue();
            return Task.FromResult<ReferenceFrameSeries?>(Fail ? null : Short ? series with { Change = [0], CutShare = [0], Correlation = [1], ShiftX = [0], ShiftY = [0], Moved = [false] } : series);
        }

        public Task<ReferenceColors?> ReadColorsAsync(string video, string work, CancellationToken cancellationToken) =>
            Task.FromResult<ReferenceColors?>(new ReferenceColors(50, 10, -5, 0.5, 0.4, ["#884422"]));
    }

    private sealed class FakeAudio : IMusicAudio
    {
        public Task<float[]?> ReadMonoAsync(string file, int sampleRate, int maxSeconds, string work, CancellationToken cancellationToken) =>
            Task.FromResult<float[]?>(null);

        public Task<bool> RenderBedAsync(string file, MusicBed bed, string output, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
