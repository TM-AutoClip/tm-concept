using Shouldly;
using TMConcept.Core.Render;
using TMConcept.Core.Speech;

namespace TMConcept.Tests.Core;

/// <summary>
/// No dead air (0.1.24): a voice's silences before, inside and after its words are cut to short natural pauses, so a video made of the
/// trimmed clips has no gap above <see cref="NarrationGaps.MaxPauseMs"/> anywhere on its timeline.
/// </summary>
public sealed class NarrationSilenceTests : IDisposable
{
    private const int Rate = 24_000;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tmc-silence-" + Guid.NewGuid().ToString("N"));

    public NarrationSilenceTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>A clip of tone and silence: positive = that many ms of a 220 Hz tone, negative = that many ms of silence.</summary>
    private string Clip(string name, params int[] parts)
    {
        var samples = new List<float>();
        foreach (var part in parts)
        {
            var count = Math.Abs(part) * Rate / 1000;
            for (var i = 0; i < count; i++)
            {
                samples.Add(part > 0 ? 0.3f * MathF.Sin(2 * MathF.PI * 220 * samples.Count / Rate) : 0f);
            }
        }

        var file = Path.Combine(_folder, name + ".wav");
        WaveFile.WritePcm16(file, samples.ToArray(), Rate);
        return file;
    }

    [Fact]
    public void LeadingTrailingAndLongInnerSilencesAreCut()
    {
        // 0.9 s before, a 1.45 s pause between two sentences (measured in a real Supertonic clip), 0.85 s after.
        var file = Clip("a", -900, 2000, -1450, 1500, -850);
        var before = WaveFile.DurationMs(file);

        var after = NarrationSilence.Trim(file);

        after.ShouldNotBeNull();
        var gaps = NarrationSilence.Gaps(file);
        gaps[0].ShouldBeLessThanOrEqualTo(NarrationGaps.LeadKeepMs + NarrationGaps.WindowMs);
        gaps[^1].ShouldBeLessThanOrEqualTo(NarrationGaps.TailKeepMs + NarrationGaps.WindowMs);
        NarrationSilence.LongestGapMs(file).ShouldBeLessThanOrEqualTo(NarrationGaps.MaxPauseMs + NarrationGaps.WindowMs);
        after!.Value.ShouldBe(WaveFile.DurationMs(file));
        (before - after.Value).ShouldBeGreaterThan(2_000, "about 0.84 + 1.0 + 0.65 s of dead air go");
        after.Value.ShouldBeGreaterThanOrEqualTo(3_500, "the spoken 3.5 s stay whole");
    }

    [Fact]
    public void ShortNaturalPausesStay()
    {
        var file = Clip("b", 1000, -300, 1000);
        var before = WaveFile.DurationMs(file);

        NarrationSilence.Trim(file).ShouldBe(before, "a 0.3 s pause and no edges: nothing to cut");
    }

    [Fact]
    public void TrimmingTwiceChangesNothing()
    {
        var file = Clip("c", -700, 800, -1200, 800, -700);
        var once = NarrationSilence.Trim(file);

        NarrationSilence.Trim(file).ShouldBe(once);
    }

    [Fact]
    public void ASilentClipKeepsOneShortPause()
    {
        var file = Clip("d", -3000);

        NarrationSilence.Trim(file).ShouldBe(NarrationGaps.MaxPauseMs);
    }

    [Fact]
    public void AnUnknownFormatIsLeftAsItIs()
    {
        // 8-bit PCM: not what any voice writes.
        var file = Path.Combine(_folder, "e.wav");
        var format = new byte[16];
        BitConverter.GetBytes((short)1).CopyTo(format, 0);
        BitConverter.GetBytes((short)1).CopyTo(format, 2);
        BitConverter.GetBytes(8000).CopyTo(format, 4);
        BitConverter.GetBytes(8000).CopyTo(format, 8);
        BitConverter.GetBytes((short)1).CopyTo(format, 12);
        BitConverter.GetBytes((short)8).CopyTo(format, 14);
        WaveFile.Write(file, format, new byte[8000]);

        NarrationSilence.Trim(file).ShouldBeNull();
        WaveFile.DurationMs(file).ShouldBe(1000);
        NarrationSilence.Gaps(file).ShouldBeEmpty();
    }

    [Fact]
    public void TheTimelineOfTrimmedScenesHasNoGapAboveTheThreshold()
    {
        // Three scenes as a voice gave them: each with silence around and inside its words.
        var clips = new[]
        {
            Clip("s1", -800, 3000, -1500, 2000, -900),
            Clip("s2", -600, 2500, -700),
            Clip("s3", -1000, 4000, -1200, 1000, -1000),
        };
        foreach (var clip in clips)
        {
            NarrationSilence.Trim(clip).ShouldNotBeNull();
        }

        var input = RenderInputBuilder.Build("t", "tr",
            clips.Select((c, i) => new RenderSceneSource("v" + i, "c" + i, string.Empty, WaveFile.DurationMs(c))).ToList(), 1080, 1920, 30);

        // Scenes follow one another without a hole, each as long as its narration (one frame of rounding), only the last one has the closing.
        var frame = 0;
        for (var i = 0; i < input.Scenes.Count; i++)
        {
            var scene = input.Scenes[i];
            scene.StartFrame.ShouldBe(frame);
            frame += scene.FrameCount;
            var audio = WaveFile.DurationMs(clips[i]) / 1000.0;
            var extra = i == input.Scenes.Count - 1 ? RenderTiming.ClosingMs / 1000.0 : 0;
            scene.DurationSeconds.ShouldBeInRange(audio + extra - 0.001, audio + extra + (1.0 / 30) + 0.001);
        }

        // What a viewer hears at every scene change: the tail of one clip and the lead of the next.
        for (var i = 0; i < clips.Length; i++)
        {
            NarrationSilence.LongestGapMs(clips[i]).ShouldBeLessThanOrEqualTo(NarrationGaps.MaxPauseMs + NarrationGaps.WindowMs);
            if (i > 0)
            {
                (NarrationSilence.Gaps(clips[i - 1])[^1] + NarrationSilence.Gaps(clips[i])[0])
                    .ShouldBeLessThanOrEqualTo(NarrationGaps.MaxPauseMs, "the pause at a scene change is short too");
            }
        }
    }
}
