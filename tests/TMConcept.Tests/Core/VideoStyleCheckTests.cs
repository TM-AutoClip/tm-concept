using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Core.Styles;

namespace TMConcept.Tests.Core;

/// <summary>
/// The template check of a video code (0.1.19): the progress bar, timers and scene counters belong to the app, a video without subtitles
/// never reads the captions, only the template's font is named, and colours outside the palette are replaced without a paid repair.
/// </summary>
public sealed class VideoStyleCheckTests
{
    private const string Ok = "window.__timelines = window.__timelines || {}; window.__timelines[\"concept\"] = gsap.timeline({ paused: true });\n";

    private static VideoStyle Style(bool subtitles = true, bool progressBar = false, string font = VideoFonts.DefaultId) =>
        new() { Name = "t", Subtitles = subtitles, ProgressBar = progressBar, FontId = font, PaletteId = VideoPalettes.DefaultId };

    private static IReadOnlyList<string> Findings(string line, VideoStyle? style = null, string language = ContentLanguages.English) =>
        VideoStyleCheck.Check(Ok + line + "\n", style ?? Style(), language).Findings;

    [Theory]
    [InlineData("const progressBar = document.createElement(\"div\");")]
    [InlineData("bar.className = \"progress-line\";")]
    [InlineData("const progress_track = makeTrack();")]
    [InlineData("const countdown = document.createElement(\"div\");")]
    [InlineData("const timerEl = document.createElement(\"div\");")]
    [InlineData("const sceneCounter = document.createElement(\"div\");")]
    [InlineData("label.textContent = (i + 1) + \" / \" + input.scenes.length;")]
    [InlineData("label.textContent = `${i + 1}/${input.scenes.length}`;")]
    [InlineData("tl.to(line, { width: \"100%\", duration: input.durationSeconds, ease: \"none\" }, 0);")]
    [InlineData("const w = interpolate(frame, [0, props.totalFrames], [0, input.width]); // scaleX")]
    public void AProgressBarTimerOrSceneCounterIsRejected(string line)
    {
        var findings = Findings(line);

        findings.ShouldHaveSingleItem().ShouldContain("progress bar, timer or scene counter");
        findings[0].ShouldStartWith("line 2:");
    }

    [Fact]
    public void TheAppOwnsTheBarAlsoWhenTheTemplateShowsOne() =>
        // On or off, the code never draws one: with the template's bar on, a drawn one would be a second bar.
        Findings("const progressBar = document.createElement(\"div\");", Style(progressBar: true)).ShouldHaveSingleItem();

    [Theory]
    [InlineData("// no progress bar here: the app draws it")]
    [InlineData("/* a countdown or a timer would be refused */")]
    [InlineData("const progress = frame / scene.frameCount;")]
    [InlineData("const t = Math.min(1, progress * 2);")]
    [InlineData("tl.to(image, { scale: 1.1, duration: input.durationSeconds, ease: \"none\" }, 0);")]
    [InlineData("const timelineAxis = document.createElement(\"div\");")]
    [InlineData("chapter.textContent = String(scene.number).padStart(2, \"0\");")]
    [InlineData("label.textContent = \"a//b\"; const x = 1;")]
    public void OrdinaryCodeAndCommentsPass(string line) => Findings(line).ShouldBeEmpty();

    [Theory]
    [InlineData("box.textContent = scene.caption;")]
    [InlineData("const text = input.scenes[0]?.caption;")]
    [InlineData("const { visual, caption } = scene;")]
    [InlineData("input.scenes.forEach(({ visual, caption }) => draw(visual, caption));")]
    [InlineData("box.textContent = scene[\"caption\"];")]
    public void WithoutSubtitlesTheCaptionIsNeverRead(string line)
    {
        Findings(line, Style(subtitles: false)).ShouldHaveSingleItem().ShouldContain("no subtitles");
        Findings(line, Style(subtitles: true)).ShouldBeEmpty("with subtitles the check does not judge the caption (it is burned in by the app)");
    }

    [Theory]
    [InlineData("// the caption is left out on purpose")]
    [InlineData("el.className = \"caption\";")]
    [InlineData("const css = \".caption { opacity: 0 }\";")]
    public void TheWordCaptionAloneIsNotAFinding(string line) => Findings(line, Style(subtitles: false)).ShouldBeEmpty();

    [Fact]
    public void ForeignFontFamiliesAreRejectedOnePerName()
    {
        var findings = Findings("root.style.fontFamily = \"Helvetica Neue, Arial, sans-serif\";");

        findings.Count.ShouldBe(2);
        findings[0].ShouldContain("\"Helvetica Neue\"");
        findings[1].ShouldContain("\"Arial\"");
        findings[0].ShouldContain("use only \"Fira Sans\"");
        Findings("ctx.font = \"bold 48px Arial\";").ShouldHaveSingleItem().ShouldContain("\"Arial\"");
        Findings("box.style.cssText = \"color:red;font-family:'Georgia',serif;\";").ShouldHaveSingleItem().ShouldContain("\"Georgia\"");
        Findings("<Txt fontFamily={'Roboto'} text={scene.visual} />").ShouldHaveSingleItem().ShouldContain("\"Roboto\"");
    }

    [Theory]
    [InlineData("root.style.fontFamily = \"Fira Sans, sans-serif\";")]
    [InlineData("box.style.cssText = \"font-family:'Fira Sans',sans-serif;font-weight:700\";")]
    [InlineData("box.style.cssText = \"font-family: var(--tm-font)\";")]
    [InlineData("root.style.fontFamily = input.style.font;")]
    [InlineData("const s = { fontFamily: `${input.style.font}, sans-serif` };")]
    [InlineData("box.style.cssText = \"font-family:\" + TM.font + \";\";")]
    [InlineData("el.style.font = \"700 64px Fira Sans\";")]
    [InlineData("root.style.fontFamily = \"inherit\";")]
    public void TheTemplateFontAndGenericFamiliesPass(string line) => Findings(line).ShouldBeEmpty();

    [Fact]
    public void ARussianVideoWithALatinOnlyFontExpectsTheCyrillicFallback()
    {
        var style = Style(font: "poppins");

        Findings("root.style.fontFamily = \"Poppins\";", style, ContentLanguages.English).ShouldBeEmpty();
        Findings("root.style.fontFamily = \"Poppins\";", style, ContentLanguages.Russian).ShouldHaveSingleItem().ShouldContain("use only \"Fira Sans\"");
    }

    [Fact]
    public void ColoursOutsideThePaletteAreReplacedByTheNearestTokenWithoutAFinding()
    {
        var palette = VideoStyleRules.Palette(Style());
        var code = Ok + "root.style.background = \"#000\";\nbox.style.color = \"#FF0000\";\nshade.style.background = \"rgba(0, 0, 0, 0.5)\";\n" +
            "keep.style.color = \"#ffb224\"; white.style.color = \"#fff\";\nlive.style.color = `rgb(${r}, 0, 0)`;\nconst el = document.querySelector(\"#root\");\n";

        var result = VideoStyleCheck.Check(code, Style(), ContentLanguages.English);

        result.Findings.ShouldBeEmpty();
        result.Code.ShouldContain("root.style.background = \"#0B1633\";", customMessage: "black becomes the background token");
        var red = PaletteColors.Value(palette, PaletteColors.Nearest(palette, new ColorValue(255, 0, 0, 1)));
        result.Code.ShouldContain("box.style.color = \"" + red + "\";");
        result.Code.ShouldContain("shade.style.background = \"rgba(11, 22, 51, 0.5)\";", customMessage: "the opacity is kept");
        result.Code.ShouldContain("keep.style.color = \"#ffb224\"; white.style.color = \"#fff\";", customMessage: "palette colours stay as written");
        result.Code.ShouldContain("`rgb(${r}, 0, 0)`", customMessage: "a colour built in code is not a literal");
        result.Code.ShouldContain("querySelector(\"#root\")");
        result.Fixes.Count.ShouldBe(3);
        result.Fixes[0].ShouldBe("#000 → #0B1633 (background)");
        VideoStyleCheck.Check(code, Style(), ContentLanguages.English).Code.ShouldBe(result.Code, "deterministic");
    }

    [Fact]
    public void TheFixedCodeStillPassesTheSafetyCheck()
    {
        var result = VideoStyleCheck.Check(Ok + "box.style.color = \"#123456\";\n", Style(), ContentLanguages.English);

        TMConcept.Core.Engines.EngineCodeCheck.Check(TMConcept.Core.Engines.EngineKind.HyperFrames, result.Code).ShouldBeEmpty();
        result.Code.ShouldNotContain("#123456");
    }

    [Fact]
    public void CommentsAreBlankedButLinesAndStringsStay()
    {
        var code = "a = 1; // progressBar\nb = \"x // y\";\n/* line\nprogressBar */ c = 2;\nd = `t // ${e}`;";

        var bare = VideoStyleCheck.WithoutComments(code);

        bare.Split('\n').Length.ShouldBe(code.Split('\n').Length);
        bare.ShouldNotContain("progressBar");
        bare.ShouldContain("b = \"x // y\";");
        bare.ShouldContain("c = 2;");
        bare.ShouldContain("d = `t // ${e}`;");
    }
}
