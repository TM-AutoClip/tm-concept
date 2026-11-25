using System.Text.Json;
using Shouldly;
using TMConcept.Core.Media;
using TMConcept.Core.Render;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;

namespace TMConcept.Tests.Core;

/// <summary>
/// The background music's level (0.1.44, C3 + F4): low under the narration, normal between scenes, never lifted in a breath shorter than
/// 0.6 s, down 0.15 s before the voice and back 0.4 s after it; the template's music settings and their defaults.
/// </summary>
public sealed class MusicEnvelopeTests : IDisposable
{
    private const double Level = 0.5;
    private const double Ducked = 0.125;
    private const int Rate = 24_000;
    private readonly string _folder = Path.Combine(TestFolders.Root, "tmc-music-" + Guid.NewGuid().ToString("N"));

    public MusicEnvelopeTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static double At(IReadOnlyList<NarrationSpan> merged, double seconds) => MusicEnvelope.Gain(merged, seconds, Level, Ducked);

    [Fact]
    public void TheMusicIsLowUnderTheNarrationAndNormalBetweenScenes()
    {
        var merged = MusicEnvelope.Merge([new NarrationSpan(1000, 3000), new NarrationSpan(6000, 8000)]);

        merged.Count.ShouldBe(2);
        At(merged, 0.2).ShouldBe(Level, "before the first sentence");
        At(merged, 2.0).ShouldBe(Ducked, "under the narration");
        At(merged, 4.5).ShouldBe(Level, "between the scenes");
        At(merged, 7.0).ShouldBe(Ducked);
        At(merged, 9.0).ShouldBe(Level, "after the last sentence");
    }

    [Fact]
    public void AShortBreathDoesNotLiftTheMusic()
    {
        // Two sentences 0.45 s apart (the longest pause a clip keeps) and two 0.59 s apart: one part each; 0.6 s apart: two parts.
        var breath = MusicEnvelope.Merge([new NarrationSpan(1000, 2000), new NarrationSpan(2450, 3500)]);
        breath.ShouldBe([new NarrationSpan(1000, 3500)]);
        At(breath, 2.2).ShouldBe(Ducked, "the music stays down in the breath");

        MusicEnvelope.Merge([new NarrationSpan(1000, 2000), new NarrationSpan(2590, 3000)]).Count.ShouldBe(1);
        var apart = MusicEnvelope.Merge([new NarrationSpan(1000, 2000), new NarrationSpan(2600, 3000)]);
        apart.Count.ShouldBe(2);

        // Between parts 0.6 s apart the ramps do not meet: it rises (release) but never above the level, and is down again at the next start.
        At(apart, 2.3).ShouldBeGreaterThan(Ducked);
        At(apart, 2.3).ShouldBeLessThanOrEqualTo(Level);
        At(apart, 2.6).ShouldBe(Ducked, 1e-9);

        // Overlapping parts and parts given out of order are joined too.
        MusicEnvelope.Merge([new NarrationSpan(5000, 6000), new NarrationSpan(1000, 3000), new NarrationSpan(2000, 4000)])
            .ShouldBe([new NarrationSpan(1000, 4000), new NarrationSpan(5000, 6000)]);
    }

    [Fact]
    public void TheEdgesTakeTheirTimes()
    {
        var merged = MusicEnvelope.Merge([new NarrationSpan(2000, 4000)]);
        var attack = MusicMixDefaults.AttackMs / 1000.0;
        var release = MusicMixDefaults.ReleaseMs / 1000.0;

        attack.ShouldBe(0.15);
        release.ShouldBe(0.4);
        At(merged, 2.0 - attack).ShouldBe(Level, 1e-9, "the ramp down starts 0.15 s before the voice");
        At(merged, 2.0 - (attack / 2)).ShouldBe((Level + Ducked) / 2, 1e-9, "half way down");
        At(merged, 2.0).ShouldBe(Ducked, 1e-9, "down when the voice starts");
        At(merged, 4.0).ShouldBe(Ducked, 1e-9, "still down when the voice ends");
        At(merged, 4.0 + (release / 2)).ShouldBe((Level + Ducked) / 2, 1e-9, "half way back");
        At(merged, 4.0 + release).ShouldBe(Level, 1e-9, "back 0.4 s after the voice");
    }

    [Fact]
    public void TheClipsAreMovedToTheirStartsOnTheTimeline()
    {
        IReadOnlyList<NarrationSpan> first = [new NarrationSpan(0, 900), new NarrationSpan(1300, 2000)];
        IReadOnlyList<NarrationSpan> second = [new NarrationSpan(60, 1500)];

        var timeline = MusicEnvelope.Timeline([(0L, first), (4200L, second)]);

        timeline.ShouldBe([new NarrationSpan(0, 900), new NarrationSpan(1300, 2000), new NarrationSpan(4260, 5700)]);
        MusicEnvelope.Merge(timeline).ShouldBe([new NarrationSpan(0, 2000), new NarrationSpan(4260, 5700)]);
    }

    [Fact]
    public void TheExpressionIsOneTermPerPartAndAConstantWithoutNarration()
    {
        MusicEnvelope.Expression([], Level, Ducked).ShouldBe("0.5");

        var merged = MusicEnvelope.Merge([new NarrationSpan(0, 1000), new NarrationSpan(4000, 5500)]);
        var expression = MusicEnvelope.Expression(merged, Level, Ducked);

        expression.ShouldBe("0.5-0.375*min(1,min(clip((t+0.15)/0.15,0,1),clip((1.4-t)/0.4,0,1))"
            + "+min(clip((t-3.85)/0.15,0,1),clip((5.9-t)/0.4,0,1)))");
        expression.ShouldNotContain(",,");
        expression.ShouldNotContain("if(", customMessage: "no nesting, however long the video");
    }

    [Fact]
    public void TheExpressionUsesTheInvariantCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
        try
        {
            MusicEnvelope.Expression(MusicEnvelope.Merge([new NarrationSpan(1250, 2500)]), Level, Ducked).ShouldNotContain(",15");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TheDefaultsPutTheMusicAboutEighteenDecibelsDownUnderTheVoice()
    {
        var (level, ducked, fade) = VideoStyleRules.MusicMix(new VideoMusic());

        level.ShouldBe(MusicEnvelope.Linear(-6), 1e-9);
        (ducked / level).ShouldBe(0.25, 0.005, "the ducking is a quarter");
        ducked.ShouldBe(0.12, 0.01, "about 0.12 (−18 dB) under the narration");
        fade.ShouldBe(2);

        // Out of bounds values (a hand-edited database) are kept inside the editor's bounds.
        var wild = VideoStyleRules.MusicMix(new VideoMusic { LevelDb = 12, DuckDb = 6, FadeOutSeconds = 99 });
        wild.Level.ShouldBe(1, 1e-9);
        wild.Ducked.ShouldBe(1, 1e-9);
        wild.FadeOutSeconds.ShouldBe(MusicMixDefaults.MaxFadeOutSeconds);
    }

    [Fact]
    public void ATemplateSavedBeforeHasTheMusicOffAndACopyKeepsTheMusic()
    {
        var old = JsonSerializer.Deserialize("{\"name\":\"Eski\"}", CoreJsonContext.Default.VideoStyle)!;
        old.Music.Source.ShouldBe(MusicSource.Off);
        VideoStyleRules.MusicOn(old).ShouldBeFalse();
        VideoStyleRules.MusicOn(null).ShouldBeFalse();

        var style = new VideoStyle { Name = "Müzikli", Music = new VideoMusic { Source = MusicSource.OwnFile, File = "C:\\m\\a.mp3", Attribution = "x", LevelDb = -9 } };
        var copy = VideoStyleRules.Copy(style);
        copy.Music.Source.ShouldBe(MusicSource.OwnFile);
        copy.Music.File.ShouldBe("C:\\m\\a.mp3");
        copy.Music.LevelDb.ShouldBe(-9);
        VideoStyleRules.MusicOn(copy).ShouldBeTrue();
        VideoStyleRules.Problems(copy).ShouldBeEmpty();
    }

    [Fact]
    public void OwnFileWithoutAFileCannotBeSaved()
    {
        var style = new VideoStyle { Name = "Müzik", Music = new VideoMusic { Source = MusicSource.OwnFile } };
        VideoStyleRules.Problems(style).ShouldBe([VideoStyleProblem.MusicFileMissing]);

        style.Music.Source = (MusicSource)99;
        VideoStyleRules.Problems(style).ShouldContain(VideoStyleProblem.UnknownValue);
    }

    [Fact]
    public void OffTheLibraryGeneratedMusicAndOwnFileAreOfferedNow() =>
        MusicSources.Available.ShouldBe([MusicSource.Off, MusicSource.MyMusic, MusicSource.Library, MusicSource.GenerateLocal, MusicSource.GenerateOnline, MusicSource.OwnFile]);

    [Fact]
    public void TheSpokenPartsOfAClipAreItsLoudRuns()
    {
        // 0.2 s silence, 0.5 s tone, 0.3 s silence, 0.4 s tone.
        var file = Path.Combine(_folder, "spoken.wav");
        var samples = new List<float>();
        foreach (var part in new[] { -200, 500, -300, 400 })
        {
            var count = Math.Abs(part) * Rate / 1000;
            for (var i = 0; i < count; i++)
            {
                samples.Add(part > 0 ? 0.3f * MathF.Sin(2 * MathF.PI * 220 * samples.Count / Rate) : 0f);
            }
        }

        WaveFile.WritePcm16(file, samples.ToArray(), Rate);

        var spoken = NarrationSilence.Spoken(file).ShouldNotBeNull();

        spoken.Count.ShouldBe(2);
        spoken[0].StartMs.ShouldBeInRange(190, 210);
        spoken[0].EndMs.ShouldBeInRange(690, 710);
        spoken[1].StartMs.ShouldBeInRange(990, 1010);
        spoken[1].EndMs.ShouldBeInRange(1390, 1410);
        MusicEnvelope.Merge(spoken).Count.ShouldBe(1, "a 0.3 s pause is a breath");
    }
}
