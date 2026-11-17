using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Engines;
using TMConcept.Core.Processes;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;
using TMConcept.Engines.Engines;
using TMConcept.Engines.Processes;
using TMConcept.Engines.Tools;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Engines;

/// <summary>
/// The engines' part of a template (0.1.19): the HyperFrames page gets the font, the colour variables and the helpers only with a template
/// (an old video's page is unchanged), Remotion loads the font before drawing, Revideo starts loading it, and both may import './tm'.
/// </summary>
public sealed class TemplateEngineTests
{
    private static IVideoEngine[] All(TempPaths paths)
    {
        IProcessRunner runner = new ProcessRunner(ChildProcessTests.Guard(), NullLogger<ProcessRunner>.Instance);
        var platform = new WindowsToolPlatform(runner);
        var tools = new ToolInstaller(paths, platform, new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance),
            NullLogger<ToolInstaller>.Instance);
        var chrome = new ChromeLocator(paths, platform, NullLogger<ChromeLocator>.Instance);
        return
        [
            new HyperFramesEngine(paths, tools, chrome, runner, NullLogger<HyperFramesEngine>.Instance),
            new RemotionEngine(paths, tools, chrome, runner, NullLogger<RemotionEngine>.Instance),
            new RevideoEngine(paths, tools, chrome, runner, NullLogger<RevideoEngine>.Instance),
        ];
    }

    private static RenderInput Styled(string font = "crimson-text", string language = ContentLanguages.English)
    {
        var input = SampleRender.Input();
        input.Language = language;
        RenderStyleBuilder.Apply(input, new VideoStyle { Name = "t", FontId = font, PaletteId = "forest", TitlePosition = TitlePosition.BottomRight });
        return input;
    }

    [Fact]
    public void TheHyperFramesPageOfAVideoWithoutATemplateIsUnchanged()
    {
        var page = HyperFramesEngine.IndexHtml(SampleRender.Input());

        page.ShouldContain("font-family: sans-serif; }\n</style>");
        page.ShouldNotContain("tm.js");
        page.ShouldNotContain("--tm-");
        page.ShouldNotContain("\"style\"");
    }

    [Fact]
    public void TheHyperFramesPageOfATemplateLoadsItsFontColoursAndHelpersBeforeTheCode()
    {
        var page = HyperFramesEngine.IndexHtml(Styled());

        page.ShouldContain("@font-face { font-family: \"Crimson Text\"; src: url(\"fonts/CrimsonText-Regular.ttf\") format(\"truetype\"); font-weight: 400;");
        page.ShouldContain("src: url(\"fonts/CrimsonText-Bold.ttf\") format(\"truetype\"); font-weight: 700;");
        page.ShouldContain("--tm-accent: #5BD17A;");
        page.ShouldContain("--tm-font: \"Crimson Text\", sans-serif;");
        page.ShouldContain("font-family: var(--tm-font); }");
        page.ShouldContain("<link rel=\"preload\" href=\"fonts/CrimsonText-Regular.ttf\" as=\"font\"");
        page.IndexOf("<script src=\"tm.js\"></script>", StringComparison.Ordinal).ShouldBeLessThan(page.IndexOf("<script src=\"scene.js\">", StringComparison.Ordinal));
        page.IndexOf("window.INPUT", StringComparison.Ordinal).ShouldBeLessThan(page.IndexOf("tm.js", StringComparison.Ordinal));

        // A single-weight font claims every weight, so the browser draws no fake bold.
        HyperFramesEngine.IndexHtml(Styled("bebas-neue")).ShouldContain("font-weight: 100 900;");
    }

    [Fact]
    public void TheHelperFilesAreWrittenNextToTheCode()
    {
        using var paths = new TempPaths();
        var engines = All(paths);
        foreach (var engine in engines)
        {
            var folder = Path.Combine(paths.WorkDirectory, engine.Name);
            engine.WriteProject(new EngineRenderRequest(folder, engine.SampleCode, Styled(), Path.Combine(folder, "out.mp4"), 1));
            File.ReadAllText(Path.Combine(folder, "input.json")).ShouldContain("\"fontRegular\": \"fonts/CrimsonText-Regular.ttf\"", customMessage: engine.Name);
        }

        File.ReadAllText(Path.Combine(paths.WorkDirectory, "HyperFrames", "tm.js")).ShouldContain("window.TM = Object.freeze(");
        File.ReadAllText(Path.Combine(paths.WorkDirectory, "Remotion", "src", "tm.tsx")).ShouldContain("export const TmTitle");
        File.ReadAllText(Path.Combine(paths.WorkDirectory, "Remotion", "src", "Root.tsx")).ShouldContain("import './fonts';");
        File.ReadAllText(Path.Combine(paths.WorkDirectory, "Revideo", "src", "tm.ts")).ShouldContain("export const tmColor");
        File.ReadAllText(Path.Combine(paths.WorkDirectory, "Revideo", "src", "project.ts")).ShouldContain("new FontFace('Crimson Text'");
    }

    [Fact]
    public void RemotionLoadsTheTemplateFontBeforeDrawingAndNothingWithoutOne()
    {
        var fonts = RemotionEngine.FontsTs(Styled());

        fonts.ShouldContain("['Crimson Text', 'fonts/CrimsonText-Regular.ttf', '400'], ['Crimson Text', 'fonts/CrimsonText-Bold.ttf', '700']");
        fonts.ShouldContain("delayRender(");
        fonts.ShouldContain("cancelRender(", customMessage: "a font that cannot load stops the render instead of drawing another font");
        RemotionEngine.FontsTs(SampleRender.Input()).ShouldContain("const faces: [string, string, string][] = [];");
        RevideoEngine.FontLoader(SampleRender.Input()).ShouldBeEmpty();
        var revideo = RevideoEngine.FontLoader(Styled());
        revideo.ShouldContain("await Promise.all(", customMessage: "the project waits for the font before any frame is drawn");
        revideo.ShouldContain("throw new Error(", customMessage: "a font that cannot load fails the render instead of drawing another font");
        revideo.ShouldNotContain("console.error(");
        RemotionEngine.InputTs.ShouldContain("style?: ConceptStyle;");
    }

    [Fact]
    public void TheHelpersMayBeImportedAndNothingElseNew()
    {
        const string remotion = "import React from 'react';\nimport {AbsoluteFill} from 'remotion';\nimport {tmColor, TmTitle} from './tm';\n" +
            "import type {ConceptInput} from './input';\nexport const Scene: React.FC<ConceptInput> = (input) => <AbsoluteFill />;\n";
        const string revideo = "import {makeScene2D} from '@revideo/2d';\nimport {tmFont} from './tm';\nexport default makeScene2D('scene', function* (view) {});\n";

        EngineCodeCheck.Check(EngineKind.Remotion, remotion).ShouldBeEmpty();
        EngineCodeCheck.Check(EngineKind.Revideo, revideo).ShouldBeEmpty();
        EngineCodeCheck.Check(EngineKind.Remotion, remotion.Replace("'./tm'", "'./tm/../../x'", StringComparison.Ordinal)).ShouldNotBeEmpty();
    }

    [Fact]
    public void TheInputCarriesTheTemplateTokensAndAnOldInputHasNone()
    {
        var input = Styled(language: ContentLanguages.Russian);

        input.Theme.Background.ShouldBe("#0E1F17", "the old theme follows the palette, so kept code recolours too");
        input.Theme.Accent.ShouldBe("#5BD17A");
        var style = input.Style.ShouldNotBeNull();
        style.Font.ShouldBe("Fira Sans", "a Russian video gets the Cyrillic fallback");
        style.TitleX.ShouldBe(1);
        style.TitleY.ShouldBe(1);
        style.SafeBottom.ShouldBe(StyleLayout.SubtitleBottom);
        style.EndCardSeconds.ShouldBe(0);
        RenderInputBuilder.ToJson(input).ShouldContain("\"colors\": {");

        var old = SampleRender.Input();
        var json = RenderInputBuilder.ToJson(old);
        RenderStyleBuilder.Apply(old, null);
        RenderInputBuilder.ToJson(old).ShouldBe(json);
        json.ShouldNotContain("\"style\"");
    }
}
