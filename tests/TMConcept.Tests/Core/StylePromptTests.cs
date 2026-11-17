using Shouldly;
using TMConcept.Core.Costs;
using TMConcept.Core.Engines;
using TMConcept.Core.Images;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;
using TMConcept.Tests.Desktop;

namespace TMConcept.Tests.Core;

/// <summary>
/// The video code prompt v9 (0.1.19): a compact template block that stays small enough for a local model, no subtitle space without
/// subtitles, the drawing style in the image prompt, the bundled fonts and the estimate that counts the block.
/// </summary>
public sealed class StylePromptTests
{
    private static VideoStyle Style(bool subtitles = true, bool progressBar = false, ClosingPlacement closing = ClosingPlacement.Off) => new()
    {
        Name = "t",
        Subtitles = subtitles,
        ProgressBar = progressBar,
        FontId = "anton",
        TitleStyle = TitleStyle.Band,
        TitlePosition = TitlePosition.MiddleLeft,
        DrawStyle = DrawStyle.StickFigure,
        Motion = MotionIntensity.Calm,
        Closing = closing,
    };

    [Fact]
    public void TheSystemTextOfATemplateHasTheBlockAndForbidsTheAppsChrome()
    {
        var engine = new VideoPageTests.FakeEngine();

        var text = VideoCodePrompt.System(engine, subtitles: true, Style(progressBar: true, closing: ClosingPlacement.Screen), ContentLanguages.English);

        VideoCodePrompt.Version.ShouldBe("9", "9: the template block");
        text.ShouldContain("Template (the user's choice");
        text.ShouldContain("ONLY input.style.font (\"Anton\")");
        text.ShouldContain("Titles: band (on a full-width band in the surface colour), at the middle left of the safe area");
        text.ShouldContain("simple stick figures");
        text.ShouldContain("Motion: calm");
        text.ShouldContain("NEVER draw a progress bar, timeline bar, timer, countdown or scene counter");
        text.ShouldContain("the app itself draws the progress bar along the top edge, the subtitles, a closing card over the last 3.5 s");
        text.ShouldContain("TM.title(text)", customMessage: "the HyperFrames helpers");
        text.ShouldNotContain("one accent colour (theme.accent)", customMessage: "the free colour sentence gives way to the palette");
    }

    [Fact]
    public void WithoutSubtitlesTheBottomIsNotKeptFree()
    {
        var engine = new VideoPageTests.FakeEngine();

        var off = VideoCodePrompt.System(engine, subtitles: true, Style(subtitles: false), ContentLanguages.English);
        var on = VideoCodePrompt.System(engine, subtitles: false, Style(subtitles: true), ContentLanguages.English);

        off.ShouldNotContain("22 %", customMessage: "the template decides, not the old checkbox");
        off.ShouldContain("There are NO subtitles");
        off.ShouldContain("8 % margin on every side.");
        on.ShouldContain("the bottom 22 % stays free for the subtitles");
        on.ShouldContain("Subtitles are burned into the bottom");
    }

    [Fact]
    public void AVideoWithoutATemplateGetsTheTextWithoutTheBlock()
    {
        var engine = new VideoPageTests.FakeEngine();

        var old = VideoCodePrompt.System(engine, subtitles: true, null, ContentLanguages.English);

        old.ShouldBe(VideoCodePrompt.System(engine, subtitles: true));
        old.ShouldNotContain("Template (");
        old.ShouldContain("one accent colour (theme.accent)");
    }

    [Theory]
    [InlineData(EngineKind.HyperFrames)]
    [InlineData(EngineKind.Remotion)]
    [InlineData(EngineKind.Revideo)]
    public void TheBlockStaysSmallForLocalModels(EngineKind kind)
    {
        var costs = new CostCalculator();
        var biggest = new[]
        {
            Style(), Style(subtitles: false, progressBar: true, closing: ClosingPlacement.Both),
        }.Max(s => costs.Tokens(VideoStylePrompt.Block(kind, s, ContentLanguages.English), ContentLanguages.English));

        // About 300-450 tokens: a local model has 8 192 tokens of context and 4 096 of output by default.
        biggest.ShouldBeLessThan(500);
        VideoStylePrompt.Block(kind, Style(), ContentLanguages.English).ShouldContain(kind == EngineKind.HyperFrames ? "window.TM" : "from './tm'");
    }

    [Fact]
    public void TheEstimateCountsTheBlock()
    {
        var estimator = new VideoCostEstimator(new CostCalculator());
        var input = RenderInputBuilder.Build("Greek fire", "en", [new RenderSceneSource("a map", "In 672 the city was under siege.", string.Empty, 2000)], 1080, 1920, 30);
        var model = new CatalogModel { Provider = "anthropic", Id = "m", Name = "m", InputUsd = 3, OutputUsd = 15, MaxOutputTokens = 64_000 };
        var engine = new VideoPageTests.FakeEngine();

        var plain = estimator.Code(input, "en", engine, model, subtitles: true);
        var styled = estimator.Code(input, "en", engine, model, subtitles: true, Style());

        styled.InputTokens.ShouldBeGreaterThan(plain.InputTokens + 200, "the block is part of the prompt");
        styled.MinUsd.ShouldBeGreaterThan(plain.MinUsd);
    }

    [Fact]
    public void TheDrawingStyleSteersTheImagePrompts()
    {
        var plain = ImagePromptBuilder.User("Byzantium", ["a map"]);
        var styled = ImagePromptBuilder.User("Byzantium", ["a map"], DrawStyle.PaperCut);

        plain.ShouldBe("Channel: «Byzantium»\nScenes:\n1. «a map»\nReturn 1 prompts.", "without a template the text is as before");
        styled.ShouldContain("Illustration style of every image: layered paper cut-out illustration");
    }

    [Fact]
    public void EveryTemplateFontShipsWithTheProgramAndDrawsTurkish()
    {
        using var paths = new TMConcept.Tests.Storage.TempPaths();
        foreach (var font in VideoFonts.All)
        {
            foreach (var file in VideoFontFiles.Files(font))
            {
                VideoFontFiles.Exists(file).ShouldBeTrue(file);
                using var stream = VideoFontFiles.Open(file);
                using var typeface = SkiaSharp.SKTypeface.FromStream(stream);
                typeface.ShouldNotBeNull(file);
                typeface.FamilyName.ShouldBe(font.Family, file);
                typeface.ContainsGlyphs("çğıİöşüÇĞÖŞÜâîû").ShouldBeTrue(file + " draws the Turkish letters");
            }

            var folder = Path.Combine(paths.WorkDirectory, font.Id);
            VideoFontFiles.CopyTo(font, folder);
            Directory.GetFiles(folder).Select(Path.GetFileName).ShouldBe(VideoFontFiles.Files(font), ignoreOrder: true);
        }

        VideoFontFiles.Embedded().Count().ShouldBe(VideoFonts.All.Sum(f => VideoFontFiles.Files(f).Count), "no font file ships unused");
        Should.Throw<InvalidOperationException>(() => VideoFontFiles.Open("Missing.ttf"));
    }
}
