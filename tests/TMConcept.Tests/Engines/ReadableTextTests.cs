using Shouldly;
using TMConcept.Core.Engines;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;
using TMConcept.Core.Subtitles;
using TMConcept.Core.Videos;
using TMConcept.Engines.Engines;

namespace TMConcept.Tests.Engines;

/// <summary>
/// Readable text (0.1.24, on by default): subtitles and on-screen text get an outline and a soft shadow in the colour opposite to the text, so
/// text the colour of its image stays readable; a template can turn it off and gets the look before.
/// </summary>
public sealed class ReadableTextTests
{
    private static readonly IReadOnlyList<SubtitleCue> Cues = [new(0, 2000, ["Over a bright sky."])];

    [Theory]
    [InlineData("#FFFFFF", "#000000")]
    [InlineData("#F1FBFF", "#000000")]
    [InlineData("#1F1F1F", "#FFFFFF")]
    [InlineData("#FFB224", "#000000")]
    [InlineData("#0B1633", "#FFFFFF")]
    public void TheOppositeColourIsBlackForLightTextAndWhiteForDark(string text, string opposite) => PaletteColors.Opposite(text).ShouldBe(opposite);

    [Fact]
    public void ContrastFollowsWcag()
    {
        PaletteColors.Contrast("#000000", "#FFFFFF").ShouldBe(21, 0.01);
        PaletteColors.Contrast("#777777", "#777777").ShouldBe(1, 0.0001);
        PaletteColors.Contrast("#FFFFFF", "#767676").ShouldBe(4.54, 0.02);
    }

    [Fact]
    public void AnOldTemplateWithoutTheFieldReadsAsOn()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(new VideoStyle { Name = "old" },
            CoreJsonContext.Default.VideoStyle))!.AsObject();
        node.Select(p => p.Key).Where(k => k.Equals("ReadableText", StringComparison.OrdinalIgnoreCase)).ToList().ForEach(k => node.Remove(k));
        var json = node.ToJsonString();
        json.ShouldNotContain("ReadableText");

        System.Text.Json.JsonSerializer.Deserialize(json, CoreJsonContext.Default.VideoStyle)!.ReadableText.ShouldBeTrue();
    }

    [Fact]
    public void SubtitlesGetAThickOppositeOutlineAndAShadow()
    {
        var on = new VideoStyle { Name = "t", Subtitles = true, PaletteId = "ocean" };
        var off = new VideoStyle { Name = "t", Subtitles = true, PaletteId = "ocean", ReadableText = false };

        var readable = StyleOverlay.Build(1080, 1920, 10, Cues, on, ContentLanguages.English, null, "c", false).Ass!;
        var plain = StyleOverlay.Build(1080, 1920, 10, Cues, off, ContentLanguages.English, null, "c", false).Ass!;

        // Ocean's text is near white: black outline (&H00000000), a 40 % see-through black shadow, outline 7 and shadow 3 at size 59.
        readable.ShouldContain("Style: Default,Fira Sans,59,&H00FFFBF1,&H000000FF,&H00000000,&H66000000,1,0,0,0,100,100,0,0,1,7,3,2,");
        plain.ShouldContain("Style: Default,Fira Sans,59,&H00FFFBF1,&H000000FF,&H002B2006,&H64000000,1,0,0,0,100,100,0,0,1,4,0,2,",
            customMessage: "off: the thin background-colour outline of 0.1.19");
    }

    [Fact]
    public void DarkTextGetsAWhiteOutline()
    {
        var paper = VideoStyleRules.FindPalette("paper")!;

        StyleOverlay.Edge(new VideoStyle(), paper, 60).Outline.ShouldBe("&H00FFFFFF");
    }

    [Fact]
    public void TheEnginesPutTheOutlineOnEveryText()
    {
        var input = SampleRender.Input();
        RenderStyleBuilder.Apply(input, new VideoStyle { Name = "t", PaletteId = "forest" });
        input.Style!.Outline.ShouldBe("#000000");

        var page = HyperFramesEngine.IndexHtml(input);
        page.ShouldContain("#root { --tm-outline: #000000; text-shadow: 0 0 0.05em #000000,");
        TemplateHelpers.RevideoHelpers.ShouldContain("export const tmOutline");
        VideoStylePrompt.Block(EngineKind.HyperFrames, new VideoStyle { Name = "t" }, ContentLanguages.English).ShouldContain("never set text-shadow yourself");
        VideoStylePrompt.Block(EngineKind.Revideo, new VideoStyle { Name = "t" }, ContentLanguages.English).ShouldContain("spread tmOutline(input)");

        var off = SampleRender.Input();
        RenderStyleBuilder.Apply(off, new VideoStyle { Name = "t", PaletteId = "forest", ReadableText = false });
        off.Style!.Outline.ShouldBeEmpty();
        HyperFramesEngine.IndexHtml(off).ShouldNotContain("text-shadow");
        VideoStylePrompt.Block(EngineKind.HyperFrames, new VideoStyle { Name = "t", ReadableText = false }, ContentLanguages.English).ShouldNotContain("Readable text");
    }
}
