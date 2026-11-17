using Shouldly;
using TMConcept.Core.Render;
using TMConcept.Core.Videos;
using TMConcept.Tests.Desktop;

namespace TMConcept.Tests.Core;

/// <summary>
/// Subtitles off means no narration text on screen at all (the developer's report, 16 Nov 2026: with subtitles off the video code still drew
/// the narration, because the prompt allowed it "small at the bottom" and the input it reads at render time carried every caption).
/// </summary>
public sealed class SubtitlesOffTests
{
    private static RenderInput Input() => RenderInputBuilder.Build("Greek fire", "en",
        [new RenderSceneSource("a map", "In 672 the city was under siege.", "1.png", 2000), new RenderSceneSource("burning ships", "A strange fire.", string.Empty, 1500)],
        1080, 1920, 30);

    [Fact]
    public void WithoutSubtitlesThePromptForbidsDrawingTheNarrationAndWithThemKeepsTheBottomFree()
    {
        var engine = new VideoPageTests.FakeEngine();

        var off = VideoCodePrompt.System(engine, subtitles: false);
        off.ShouldContain("There are NO subtitles");
        off.ShouldContain("NEVER draw the caption (narration) text or any part of it");
        off.ShouldContain("never write the spoken sentences into the code; the narration is left out of the input on purpose");
        off.ShouldContain("labels, numbers or years that belong to a scene's visual idea are fine.");
        off.ShouldNotContain("may appear small at the bottom");

        var on = VideoCodePrompt.System(engine, subtitles: true);
        on.ShouldContain("Subtitles are burned into the bottom of the finished video afterwards: NEVER draw the caption (narration) text yourself");
        on.ShouldContain("bottom 22 %");
        VideoCodePrompt.Version.ShouldBe("9");
    }

    [Fact]
    public void TheEngineInputWithoutSubtitlesIsACopyWithoutCaptions()
    {
        var input = Input();

        RenderInputBuilder.ForEngine(input, subtitles: true).ShouldBeSameAs(input);
        var engine = RenderInputBuilder.ForEngine(input, subtitles: false);

        engine.ShouldNotBeSameAs(input);
        engine.Scenes.ShouldAllBe(s => s.Caption.Length == 0);
        engine.Scenes.Select(s => s.Visual).ShouldBe(["a map", "burning ships"]);
        engine.Scenes[0].Image.ShouldBe("1.png");
        engine.Scenes.Select(s => s.StartFrame).ShouldBe(input.Scenes.Select(s => s.StartFrame));
        engine.TotalFrames.ShouldBe(input.TotalFrames);
        input.Scenes[0].Caption.ShouldBe("In 672 the city was under siege.", "the video's own input keeps its narration");
    }

    [Fact]
    public void ThePromptOfAnInputWithoutCaptionsNamesNoCaption()
    {
        var input = Input();

        VideoCodePrompt.User(input).ShouldContain("caption «In 672 the city was under siege.»");
        var off = VideoCodePrompt.User(RenderInputBuilder.ForEngine(input, subtitles: false));
        off.ShouldNotContain("caption");
        off.ShouldNotContain("siege");
        off.ShouldContain("visual «a map»; image \"1.png\"");
    }
}
