using Shouldly;
using TMConcept.Core.Render;
using TMConcept.Core.Speech;
using TMConcept.Core.Subtitles;

namespace TMConcept.Tests.Core;

public sealed class SubtitleTests
{
    private static readonly SubtitleSource[] Scenes =
    [
        new(0, 6000, "In 672 the city was under siege. A strange fire burned the ships on the water, and nobody could put it out."),
        new(6000, 3000, "Its recipe is still a secret."),
    ];

    [Fact]
    public void CuesHaveAtMostTwoShortLinesAndStayInsideTheirScene()
    {
        var cues = SubtitleBuilder.Build(Scenes);

        cues.ShouldNotBeEmpty();
        cues.ShouldAllBe(c => c.Lines.Count <= 2 && c.Lines.All(l => l.Length <= SubtitleBuilder.LineMax));
        cues.Zip(cues.Skip(1)).ShouldAllBe(p => p.First.EndMs <= p.Second.StartMs);
        cues.Where(c => c.StartMs < 6000).ShouldAllBe(c => c.EndMs <= 6000);
        cues.Last().EndMs.ShouldBeLessThanOrEqualTo(9000);
    }

    [Fact]
    public void SrtAndAssAreWellFormed()
    {
        var cues = SubtitleBuilder.Build(Scenes);

        var srt = SubtitleBuilder.Srt(cues);
        srt.ShouldStartWith("1\n00:00:00,000 --> ");
        var ass = SubtitleBuilder.Ass(cues, 1080, 1920, new RenderTheme());
        ass.ShouldContain("PlayResX: 1080");
        ass.ShouldContain("Dialogue: 0,0:00:00.00,");
        ass.ShouldContain("&H00FFFFFF");
    }

    [Fact]
    public void TheNarrationFileListsEveryScene()
    {
        var text = NarrationScript.Text("Greek fire", "tr", Scenes);

        text.ShouldContain("Sahne 1 (≈ 6.0 sn)");
        text.ShouldContain("Sahne 2 (≈ 3.0 sn)");
    }

    [Fact]
    public void AWavHeaderGivesTheLength()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            const int rate = 24000;
            writer.Write("RIFF"u8);
            writer.Write(36 + (rate * 2));
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(rate * 2);
            writer.Write(new byte[rate * 2]);
        }

        stream.Position = 0;
        WaveFile.DurationMs(stream).ShouldBe(1000);
    }

    [Fact]
    public void TheCacheKeyChangesWithEveryInput()
    {
        var a = SpeechService.Key("hello", "supertonic", "", "1", 1.0);
        SpeechService.Key("hello", "supertonic", "", "1", 1.0).ShouldBe(a);
        SpeechService.Key("hello", "supertonic", "", "2", 1.0).ShouldNotBe(a);
        SpeechService.Key("hello", "supertonic", "", "1", 1.1).ShouldNotBe(a);
        SpeechService.Key("hello!", "supertonic", "", "1", 1.0).ShouldNotBe(a);
    }
}
