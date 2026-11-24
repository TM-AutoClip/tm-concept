using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;
using TMConcept.Core.Subtitles;
using TMConcept.Core.Tools;
using TMConcept.Core.Videos;
using TMConcept.Engines.Recognition;
using TMConcept.Engines.Tools;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.48 (F5 + F6, S3): the overflow margin of a growing highlighted word, the alignment of the script on heard word times, the grouping by
/// the breath of the speech, the reading of whisper.cpp's JSON and the subtitles staying exactly as they were without whisper.cpp.
/// </summary>
public sealed class WordTimingTests
{
    private static RecognizedWord H(string text, long start, long end) => new(text, start, end);

    private static TimedWord W(string text, long start, long end) => new(text, start, end);

    private static List<string> Texts(IReadOnlyList<SubtitleCue> cues) => cues.Select(c => string.Join(" | ", c.Lines)).ToList();

    [Fact]
    public void AGrowingHighlightShortensTheLineByItsGrowth()
    {
        SubtitleBuilder.LineLength(1080, 1920).ShouldBe(26);
        SubtitleBuilder.LineLength(1080, 1920, WordHighlight.Grow).ShouldBe(23, "the width is divided by 1.12");
        SubtitleBuilder.LineLength(1920, 1080, WordHighlight.Grow).ShouldBe(SubtitleBuilder.LineMax, "a wide picture still fits the most characters");

        WordHighlight.GrowPercent(new VideoStyle { WordHighlight = true, DrawStyle = DrawStyle.Cartoon }).ShouldBe(112);
        WordHighlight.GrowPercent(new VideoStyle { WordHighlight = true, DrawStyle = DrawStyle.Neon }).ShouldBe(100, "neon glows, it does not grow");
        WordHighlight.GrowPercent(new VideoStyle { WordHighlight = false, DrawStyle = DrawStyle.Cartoon }).ShouldBe(100);
        WordHighlight.GrowPercent(new VideoStyle { WordHighlight = true, Subtitles = false, DrawStyle = DrawStyle.Cartoon }).ShouldBe(100);
        WordHighlight.GrowPercent(null).ShouldBe(100);
    }

    [Fact]
    public void HeardWordsGiveTheScriptTheirTimes()
    {
        var sentences = WordAligner.Align("The city fell at dawn.",
            [H("The", 0, 200), H("city", 200, 600), H("fell", 600, 900), H("at", 900, 1000), H("dawn.", 1000, 1500)], 0, 2000)!;
        sentences.Count.ShouldBe(1);
        sentences[0].MatchRatio.ShouldBe(1.0);
        sentences[0].NeedsListening.ShouldBeFalse();
        var fell = sentences[0].Words[2];
        (fell.Text, fell.StartMs, fell.EndMs, fell.Interpolated).ShouldBe(("fell", 600L, 900L, false));
        sentences[0].Words.Select(w => w.Text).ShouldBe(["The", "city", "fell", "at", "dawn."], "the text stays the script's");
    }

    [Fact]
    public void AWordTheVoiceSkippedIsPlacedBetweenItsNeighbours()
    {
        var sentence = WordAligner.Align("The city fell at dawn.", [H("The", 0, 200), H("city", 200, 600), H("at", 1000, 1100), H("dawn", 1100, 1500)], 0, 2000)![0];
        var fell = sentence.Words[2];
        fell.Interpolated.ShouldBeTrue();
        fell.StartMs.ShouldBe(600);
        fell.EndMs.ShouldBe(1000);
        sentence.Words[3].StartMs.ShouldBe(1000);
        sentence.MatchRatio.ShouldBe(13 / 17.0, 0.001);
        sentence.NeedsListening.ShouldBeFalse();
    }

    [Fact]
    public void AnExtraHeardWordIsIgnored()
    {
        var sentence = WordAligner.Align("The city fell.", [H("The", 0, 200), H("um", 200, 400), H("city", 400, 800), H("fell", 800, 1100)], 0, 1500)![0];
        sentence.Words.Select(w => w.Text).ShouldBe(["The", "city", "fell."]);
        sentence.Words[1].StartMs.ShouldBe(400);
        sentence.Words.ShouldAllBe(w => !w.Interpolated);
        sentence.MatchRatio.ShouldBe(1.0);
    }

    [Fact]
    public void TurkishDottedAndDotlessIMatchWhateverTheCase()
    {
        AlignmentText.Fold("İSTANBUL'U").ShouldBe("istanbulu");
        AlignmentText.Fold("IŞIK").ShouldBe(AlignmentText.Fold("ışık"));
        var sentence = WordAligner.Align("İSTANBUL'U IŞIK.", [H("istanbul'u", 0, 800), H("ışık", 800, 1200)], 0, 1500)![0];
        sentence.MatchRatio.ShouldBe(1.0);
        sentence.Words[1].StartMs.ShouldBe(800);
        sentence.Words[0].Text.ShouldBe("İSTANBUL'U");
    }

    [Fact]
    public void ASentenceHeardPoorlyIsMarkedForListening()
    {
        var sentences = WordAligner.Align("Bizans çöktü. Konstantinopolis düştü.",
            [H("bizans", 0, 500), H("çöktü", 500, 900), H("xyz", 1000, 1500), H("qq", 1500, 1800)], 0, 2000)!;
        sentences.Count.ShouldBe(2);
        sentences[0].NeedsListening.ShouldBeFalse();
        sentences[1].MatchRatio.ShouldBeLessThan(CaptionTiming.MatchThreshold);
        sentences[1].NeedsListening.ShouldBeTrue();
        sentences[1].Words.ShouldAllBe(w => w.Interpolated);
        sentences[1].Words[0].StartMs.ShouldBeGreaterThanOrEqualTo(sentences[0].Words[^1].EndMs, "the order holds");
        SubtitleTimingStep.CheckText(sentences).ShouldContain("Konstantinopolis düştü.");
        SubtitleTimingStep.CheckText(sentences).ShouldNotContain("Bizans");
    }

    [Fact]
    public void ASentenceEndAlwaysStartsANewGroup()
    {
        var cues = PauseGrouper.Group([W("One", 0, 300), W("two.", 300, 600), W("Three", 700, 1000), W("four", 1000, 1300)], "en", 42, 0, 5000);
        Texts(cues).ShouldBe(["One two.", "Three four"]);
        cues[0].Words.Count.ShouldBe(2);
    }

    [Fact]
    public void APauseOfHalfASecondStartsANewGroupAndTheTimesFollowTheRules()
    {
        var cues = PauseGrouper.Group([W("a", 0, 300), W("b", 300, 600), W("c", 1100, 1400), W("d", 1400, 1700)], "en", 42, 0, 5000);
        Texts(cues).ShouldBe(["a b", "c d"]);
        cues[0].StartMs.ShouldBe(0, "never before the part's start");
        cues[1].StartMs.ShouldBe(1100 - CaptionTiming.LeadMs);
        cues[0].EndMs.ShouldBe(1100 - CaptionTiming.LeadMs - CaptionTiming.GapMs, "the earlier of the next group's start - 50 and the last word + 600");
        cues[1].EndMs.ShouldBe(1700 + CaptionTiming.TailMs);
        PauseGrouper.Group([W("a", 0, 300), W("b", 300, 600)], "en", 42, 0, 800)[0].EndMs.ShouldBe(800, "never past the part's end");
    }

    [Fact]
    public void ACommaBreaksOnlyWithItsPauseAndOnlyOnceTheGroupIsLongEnough()
    {
        Texts(PauseGrouper.Group([W("Once", 0, 300), W("upon,", 300, 700), W("a", 950, 1100), W("time", 1100, 1500)], "en", 42, 0, 5000))
            .ShouldBe(["Once upon,", "a time"]);
        Texts(PauseGrouper.Group([W("Once", 0, 300), W("upon,", 300, 700), W("a", 800, 1100), W("time", 1100, 1500)], "en", 42, 0, 5000))
            .ShouldBe(["Once upon, a time"], "a short breath after the comma");
        Texts(PauseGrouper.Group([W("Well,", 0, 600), W("a", 900, 1000), W("b", 1000, 1300)], "en", 42, 0, 5000))
            .ShouldBe(["Well, a b"], "a group of one word waits for a second");
    }

    [Fact]
    public void AConjunctionStartsANewGroupInTheNarrationsLanguage()
    {
        Texts(PauseGrouper.Group([W("I", 0, 200), W("came", 200, 600), W("and", 650, 800), W("left", 800, 1200)], "en", 42, 0, 5000))
            .ShouldBe(["I came", "and left"]);
        Texts(PauseGrouper.Group([W("Geldi", 0, 400), W("gördü", 400, 800), W("ama", 850, 1000), W("gitti", 1000, 1400)], ContentLanguages.Turkish, 42, 0, 5000))
            .ShouldBe(["Geldi gördü", "ama gitti"]);
        Texts(PauseGrouper.Group([W("I", 0, 200), W("came", 200, 600), W("and", 650, 800), W("left", 800, 1200)], ContentLanguages.German, 42, 0, 5000))
            .ShouldBe(["I came and left"], "no list for this language");
    }

    [Fact]
    public void SixWordsOrTwoAndAHalfSecondsEndAGroup()
    {
        var eight = Enumerable.Range(1, 8).Select(i => W("w" + i, (i - 1) * 200, i * 200)).ToList();
        Texts(PauseGrouper.Group(eight, "en", 42, 0, 5000)).ShouldBe(["w1 w2 w3 w4 w5 w6", "w7 w8"]);
        var slow = new List<TimedWord> { W("a", 0, 800), W("b", 800, 1600), W("c", 1600, 2400), W("d", 2400, 3200) };
        Texts(PauseGrouper.Group(slow, "en", 42, 0, 5000)).ShouldBe(["a b c", "d"]);
    }

    [Fact]
    public void AWordThatWouldNeedAThirdLineStartsANewGroup()
    {
        var words = Enumerable.Range(0, 3).Select(i => W("abcdefgh", i * 100, (i + 1) * 100)).ToList();
        var cues = PauseGrouper.Group(words, "en", 10, 0, 5000);
        cues.Count.ShouldBe(2);
        cues[0].Lines.ShouldBe(["abcdefgh", "abcdefgh"]);
    }

    [Fact]
    public void TheHighlightUsesHeardTimesWhenTheCueHasThem()
    {
        var cue = new SubtitleCue(900, 3000, ["a longword b"]) { Words = [W("a", 1000, 1100), W("longword", 1100, 1300), W("b", 2500, 2700)] };
        var steps = WordHighlight.Steps(cue, "<", ">");
        steps.Select(s => (s.StartMs, s.EndMs)).ShouldBe([(900L, 1100L), (1100L, 2500L), (2500L, 3000L)]);
    }

    [Fact]
    public void WhisperCppsJsonGivesTheWordsWithoutMarkers()
    {
        const string json = """
            {"systeminfo":"AVX = 1","model":{"type":"base"},"params":{"language":"tr"},"result":{"language":"tr"},"transcription":[
              {"timestamps":{"from":"00:00:00,000","to":"00:00:00,000"},"offsets":{"from":0,"to":0},"text":""},
              {"timestamps":{"from":"00:00:00,000","to":"00:00:00,420"},"offsets":{"from":0,"to":420},"text":" İstanbul"},
              {"timestamps":{"from":"00:00:00,420","to":"00:00:00,800"},"offsets":{"from":420,"to":800},"text":" fethedildi."},
              {"timestamps":{"from":"00:00:00,800","to":"00:00:01,200"},"offsets":{"from":800,"to":1200},"text":" [BLANK_AUDIO]"},
              {"timestamps":{"from":"00:00:01,200","to":"00:00:01,250"},"offsets":{"from":1200,"to":1250},"text":" ."}
            ]}
            """;
        var words = WhisperJson.Words(json);
        words.ShouldBe([H("İstanbul", 0, 420), H("fethedildi.", 420, 800)]);
        Should.Throw<FormatException>(() => WhisperJson.Words("{\"result\":{}}"));
        Should.Throw<FormatException>(() => WhisperJson.Words("not json"));
    }

    [Fact]
    public void WhisperCliIsAskedForOneWordPerEntryInTheNarrationsLanguage()
    {
        var arguments = WhisperCppRecognizer.Arguments("m.bin", "a.wav", "out", "TR");
        arguments.ShouldContain("--split-on-word");
        string.Join(' ', arguments).ShouldContain("-l tr");
        string.Join(' ', arguments).ShouldContain("--max-len 1");
        string.Join(' ', arguments).ShouldContain("--output-file out");
        string.Join(' ', WhisperCppRecognizer.Arguments("m.bin", "a.wav", "out", "")).ShouldContain("-l auto");
    }

    [Fact]
    public void WhisperCppIsPinnedForWindowsOnlyAndItsModelToACommit()
    {
        ToolManifest.Archives.Where(a => a.Tool == ToolKind.SpeechRecognizer).Select(a => a.Rid).ShouldBe(["win-x64"]);
        ToolManifest.Archives.Single(a => a.Tool == ToolKind.SpeechRecognizer).Sha256.Length.ShouldBe(64);
        ToolManifest.Libraries.ShouldContain(ToolKind.SpeechRecognizer, "never taken from the PATH");
        RecognizerModels.File.Url.AbsoluteUri.ShouldContain("/resolve/" + RecognizerModels.Commit + "/");
        RecognizerModels.File.Sha256.Length.ShouldBe(64);
    }

    private static (RenderInput Input, List<SpeechClip?> Clips, string Folder) Video()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tmc-words-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var first = Path.Combine(folder, "scene-1.wav");
        var second = Path.Combine(folder, "scene-2.wav");
        File.WriteAllBytes(first, [0]);
        File.WriteAllBytes(second, [0]);
        var input = new RenderInput
        {
            Width = 1080,
            Height = 1920,
            Scenes =
            [
                new RenderScene { Number = 1, StartSeconds = 0, DurationSeconds = 3, Caption = "The city fell at dawn. The walls were silent." },
                new RenderScene { Number = 2, StartSeconds = 3, DurationSeconds = 2, Caption = "A new age began." },
            ],
        };
        return (input, [new SpeechClip(first, 3000, 40, 0, "test"), new SpeechClip(second, 2000, 16, 0, "test")], folder);
    }

    private static List<(long, long, string)> Shape(IEnumerable<SubtitleCue> cues) => cues.Select(c => (c.StartMs, c.EndMs, string.Join("|", c.Lines))).ToList();

    [Fact]
    public async Task WithoutWhisperCppTheSubtitlesAreExactlyAsBefore()
    {
        var (input, clips, folder) = Video();
        try
        {
            foreach (var recognizer in new[] { new FakeWordRecognizer(supported: true, ready: false), new FakeWordRecognizer(supported: false, ready: true) })
            {
                var step = new SubtitleTimingStep(recognizer, NullLogger<SubtitleTimingStep>.Instance);
                var result = await step.BuildAsync(input, clips, "en", 26, CancellationToken.None);
                Shape(result.Cues).ShouldBe(Shape(SubtitleBuilder.Build(VideoAssembler.SubtitleSources(input, clips), 26)));
                result.Cues.ShouldAllBe(c => c.Words.Count == 0);
                result.Warnings.ShouldBeEmpty();
                result.Heard.ShouldBeFalse();
                recognizer.Heard.ShouldBeEmpty("nothing is heard without the component");
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task HeardClipsGiveGroupedCuesOnTheVideosTime()
    {
        var (input, clips, folder) = Video();
        try
        {
            var recognizer = new FakeWordRecognizer(true, true, file => file.EndsWith("scene-1.wav", StringComparison.Ordinal)
                ? [H("The", 100, 300), H("city", 300, 700), H("fell", 700, 1000), H("at", 1000, 1100), H("dawn", 1100, 1500), H("the", 2000, 2200), H("walls", 2200, 2500), H("were", 2500, 2650), H("silent", 2650, 2950)]
                : [H("a", 100, 200), H("new", 200, 450), H("age", 450, 800), H("began", 800, 1300)]);
            var step = new SubtitleTimingStep(recognizer, NullLogger<SubtitleTimingStep>.Instance);
            var result = await step.BuildAsync(input, clips, "en", 26, CancellationToken.None);
            recognizer.Heard.Count.ShouldBe(2);
            result.Heard.ShouldBeTrue();
            result.Warnings.ShouldBeEmpty();
            Texts(result.Cues).ShouldBe(["The city fell at dawn.", "The walls were silent.", "A new age began."]);
            result.Cues[0].StartMs.ShouldBe(100 - CaptionTiming.LeadMs);
            result.Cues[2].StartMs.ShouldBe(3000 + 100 - CaptionTiming.LeadMs, "the second scene's words on the video's time");
            result.Cues[2].Words[0].StartMs.ShouldBe(3100);
            result.Cues.ShouldAllBe(c => c.Words.Count > 0);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task AClipThatCannotBeHeardKeepsItsEstimateAndSaysSo()
    {
        var (input, clips, folder) = Video();
        try
        {
            var recognizer = new FakeWordRecognizer(true, true, file => file.EndsWith("scene-2.wav", StringComparison.Ordinal)
                ? throw new InvalidOperationException("whisper.cpp ended with code 3")
                : [H("The", 100, 300), H("city", 300, 700), H("fell", 700, 1000), H("at", 1000, 1100), H("dawn", 1100, 1500)]);
            var step = new SubtitleTimingStep(recognizer, NullLogger<SubtitleTimingStep>.Instance);
            var result = await step.BuildAsync(input, clips, "en", 26, CancellationToken.None);
            result.Warnings.ShouldContain(VideoWarnings.SubtitleEstimated);
            result.Warnings.ShouldContain(VideoWarnings.SubtitleListen, "the second sentence of scene 1 was not heard");
            var estimated = SubtitleBuilder.Build(VideoAssembler.SceneSources(input.Scenes[1], clips[1]), 26);
            Shape(result.Cues.Where(c => c.StartMs >= 3000)).ShouldBe(Shape(estimated));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task AVoiceThatGivesItsWordTimesIsNotHeardAgain()
    {
        var (input, clips, folder) = Video();
        try
        {
            clips[1] = clips[1]! with { Words = [H("A", 0, 100), H("new", 100, 300), H("age", 300, 600), H("began.", 600, 1000)] };
            var recognizer = new FakeWordRecognizer(true, false);
            var step = new SubtitleTimingStep(recognizer, NullLogger<SubtitleTimingStep>.Instance);
            var result = await step.BuildAsync(input, clips, "en", 26, CancellationToken.None);
            recognizer.Heard.ShouldBeEmpty();
            result.Cues.Single(c => c.StartMs >= 3000).Words.Count.ShouldBe(4);
            result.Cues.Where(c => c.StartMs < 3000).ShouldAllBe(c => c.Words.Count == 0, "the other scene keeps its estimate");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
