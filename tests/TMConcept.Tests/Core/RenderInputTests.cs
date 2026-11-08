using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;

namespace TMConcept.Tests.Core;

public sealed class RenderInputTests
{
    [Fact]
    public void FramesFollowAudioWithAClosingMargin()
    {
        var input = RenderInputBuilder.Build("t", "en", [new("v1", "c1", "", 1500), new("v2", "c2", "", 200)], 1080, 1920, 30);
        input.Scenes[0].FrameCount.ShouldBe(45);
        input.Scenes[1].StartFrame.ShouldBe(45);
        input.Scenes[1].FrameCount.ShouldBe(60);
        input.TotalFrames.ShouldBe(105);
        input.DurationSeconds.ShouldBe(3.5);
    }

    [Fact]
    public void JsonIsCamelCaseAndRoundTrips()
    {
        var input = RenderInputBuilder.Build("Başlık", "tr", [new("ğüş", "çö", "images/1.png", 1000)], 540, 960, 30);
        var json = RenderInputBuilder.ToJson(input);
        json.ShouldContain("\"totalFrames\"");
        json.ShouldContain("Başlık");
        RenderInputBuilder.FromJson(json)!.Scenes.Single().Image.ShouldBe("images/1.png");
    }

    [Fact]
    public void HtmlSafeJsonCannotCloseTheScriptBlock()
    {
        var input = RenderInputBuilder.Build("</script><script>alert(1)</script>", "en", [new("v", "c", "", 1000)], 2, 2, 30);
        RenderInputBuilder.ToHtmlSafeJson(input).ShouldNotContain("</script>");
    }

    [Fact]
    public void SubtitlesFollowTheSpokenPartsAroundThePauses()
    {
        var input = RenderInputBuilder.Build("t", "en", [new("v1", "Bizans düştü. Sonra sessizlik.", "", 5000), new("v2", "Plain.", "", 1000)], 1080, 1920, 30);
        var paused = new TMConcept.Core.Speech.SpeechClip("1.wav", 5000, 20, 0, "x")
        {
            Spans = [new(2000, 1500, "Bizans düştü."), new(3500, 1500, "Sonra sessizlik.")],
        };
        var plain = new TMConcept.Core.Speech.SpeechClip("2.wav", 1000, 6, 0, "x");

        var sources = TMConcept.Core.Videos.VideoAssembler.SubtitleSources(input, [paused, plain]);

        sources.Count.ShouldBe(3);
        sources[0].ShouldBe(new TMConcept.Core.Subtitles.SubtitleSource(2000, 1500, "Bizans düştü."), "no subtitle during the opening pause");
        sources[1].StartMs.ShouldBe(3500);
        var second = (long)Math.Round(input.Scenes[1].StartSeconds * 1000);
        sources[2].ShouldBe(new TMConcept.Core.Subtitles.SubtitleSource(second, 1000, "Plain."));
    }

    [Fact]
    public void SizesAreEven()
    {
        RenderInputBuilder.Size(VideoFormat.Vertical).ShouldBe((1080, 1920));
        RenderInputBuilder.Size(VideoFormat.Horizontal, 0.5).ShouldBe((960, 540));
    }
}
