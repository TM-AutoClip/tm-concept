using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Core.Styles;
using TMConcept.Core.Subtitles;

namespace TMConcept.Tests.Core;

/// <summary>0.1.34: word-by-word highlighted subtitles, in the template's colours and the drawing style's way.</summary>
public sealed class WordHighlightTests
{
    private static string Ass(VideoStyle style, params SubtitleCue[] cues) =>
        StyleOverlay.Build(1080, 1920, 10, cues, style, ContentLanguages.English, null, "c", false).Ass!;

    private static List<string> Dialogues(string ass) => ass.Split('\n').Where(l => l.StartsWith("Dialogue: 0,", StringComparison.Ordinal)).ToList();

    [Fact]
    public void EveryWordGetsItsOwnStepInTheAccentAndTheStepsCoverTheCue()
    {
        var style = new VideoStyle { WordHighlight = true, PaletteId = "paper", DrawStyle = DrawStyle.PencilSketch };
        var palette = VideoStyleRules.Palette(style);
        var lines = Dialogues(Ass(style, new SubtitleCue(1000, 3000, ["The city", "fell {at} dawn"])));
        lines.Count.ShouldBe(5, "one step per word");
        var accent = StyleOverlay.Tag(palette.Accent);
        lines[0].ShouldContain("0:00:01.00,");
        lines[0].ShouldContain(@"{\1c" + accent + "}The{");
        lines[2].ShouldContain(@"\N{\1c" + accent + "}fell{", Case.Sensitive, "the line break stays");
        lines[3].ShouldContain("(at)", Case.Sensitive, "braces of the text cannot open a tag");
        lines[^1].ShouldContain(",0:00:03.00,");
        lines.ShouldAllBe(l => !l.Contains(@"\fscx", StringComparison.Ordinal) && !l.Contains(@"\blur", StringComparison.Ordinal), "a drawn look only changes the colour");

        var steps = WordHighlight.Steps(new SubtitleCue(0, 1000, ["a longword"]), "<", ">");
        steps.Select(s => s.Text).ShouldBe(["<a> longword", "a <longword>"]);
        steps[0].EndMs.ShouldBe(steps[1].StartMs);
        (steps[1].EndMs - steps[1].StartMs).ShouldBeGreaterThan(steps[0].EndMs - steps[0].StartMs, "a longer word stays longer");
    }

    [Fact]
    public void TheHighlightFollowsTheStyleAndIsOffByDefault()
    {
        Dialogues(Ass(new VideoStyle(), new SubtitleCue(0, 1000, ["two words"]))).Count.ShouldBe(1, "off by default: one subtitle line");
        Ass(new VideoStyle { WordHighlight = true, DrawStyle = DrawStyle.Neon }, new SubtitleCue(0, 1000, ["two words"])).ShouldContain(@"\blur3");
        Ass(new VideoStyle { WordHighlight = true, DrawStyle = DrawStyle.Cartoon }, new SubtitleCue(0, 1000, ["two words"]))
            .ShouldContain(@"\fscx" + WordHighlight.Grow);
        StyleOverlay.Build(1080, 1920, 10, [new SubtitleCue(0, 1000, ["x y"])], new VideoStyle { WordHighlight = true, Subtitles = false },
            ContentLanguages.English, null, "c", false).Ass.ShouldBeNull("no subtitles, no highlight");
    }
}
