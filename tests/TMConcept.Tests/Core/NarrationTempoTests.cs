using Shouldly;
using TMConcept.Core.Markers;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;

namespace TMConcept.Tests.Core;

/// <summary>0.1.31: the narration breathes between sentences and scenes (template tempo), and a sentence is never cut at an abbreviation.</summary>
public sealed class NarrationTempoTests
{
    [Fact]
    public void SentencesEndAtTheirMarksOnly()
    {
        SentenceSplitter.Split("Bizans düştü. Sonra sessizlik geldi! Neden? Bilmiyoruz…  Belki de.")
            .ShouldBe(["Bizans düştü.", "Sonra sessizlik geldi!", "Neden?", "Bilmiyoruz…", "Belki de."]);
    }

    [Theory]
    [InlineData("II. Mehmed kenti aldı. Sonra döndü.", 2)]
    [InlineData("1. Dünya Savaşı başladı. Herkes şaşırdı.", 2)]
    [InlineData("Dr. Ahmet geldi. M.Ö. 330 yılıydı.", 2)]
    [InlineData("A. Smith wrote it. Then he left.", 2)]
    [InlineData("\"Gel!\" dedi. Sonra sustu.", 2)]
    [InlineData("küçük harfle başlayan. devam eder", 1)]
    [InlineData("", 0)]
    public void AbbreviationsNumbersAndRomanNumeralsDoNotSplit(string text, int count) => SentenceSplitter.Split(text).Count.ShouldBe(count);

    [Fact]
    public void EverySentenceButTheLastGetsTheSentenceSilence()
    {
        var pacing = new NarrationPacing(300, 650);
        var parts = SceneNarrator.Sentences(MarkerText.Parts("Bir. İki. Üç.", out _), pacing);
        parts.Select(p => p.Text).ShouldBe(["Bir.", "İki.", "Üç."]);
        parts.Select(p => p.PauseAfterMs).ShouldBe([300, 300, 0]);
    }

    [Fact]
    public void APlaceholderPauseIsAddedOnTopAndASpeedChangeDoesNotGlueSentences()
    {
        var pacing = new NarrationPacing(300, 650);
        var parts = SceneNarrator.Sentences(MarkerText.Parts("Bir. [duraklama 1 sn] İki. [yavaş] Üç.", out _), pacing);
        parts.Select(p => p.PauseAfterMs).ShouldBe([1300, 300, 0]);
        parts[2].Speed.ShouldBe(MarkerCatalog.SlowSpeed);
    }

    [Fact]
    public void ASpeedChangeInsideASentenceAddsNoSilence()
    {
        var parts = SceneNarrator.Sentences(MarkerText.Parts("Ve sonra [yavaş] çok yavaş.", out _), new NarrationPacing(300, 650));
        parts.Select(p => p.PauseAfterMs).ShouldBe([0, 0]);
    }

    [Fact]
    public void TemposGrowFromTightToRelaxedAndNaturalIsTheDefault()
    {
        var tight = NarrationTempos.Of(NarrationTempo.Tight);
        var natural = NarrationTempos.Of(NarrationTempo.Natural);
        var relaxed = NarrationTempos.Of(NarrationTempo.Relaxed);
        tight.SentenceGapMs.ShouldBeLessThan(natural.SentenceGapMs);
        natural.SentenceGapMs.ShouldBeLessThan(relaxed.SentenceGapMs);
        tight.SceneGapMs.ShouldBeLessThan(natural.SceneGapMs);
        natural.SceneGapMs.ShouldBeLessThan(relaxed.SceneGapMs);
        new VideoStyle().Tempo.ShouldBe(NarrationTempo.Natural);
        VideoStyleRules.Pacing(null).ShouldBe(natural);
        NarrationTempos.Of((NarrationTempo)99).ShouldBe(natural);
    }

    [Theory]
    [InlineData(NarrationTempo.Tight, 150, 200)]
    [InlineData(NarrationTempo.Natural, 250, 300)]
    [InlineData(NarrationTempo.Relaxed, 400, 500)]
    public void WhatAListenerHearsIsTheTempoTotal(NarrationTempo tempo, int sentenceMs, int sceneMs)
    {
        // 0.1.62, the developer: "0.25 s is enough". The added silence plus the margins every trimmed clip keeps (the tail of one clip, the
        // lead of the next) is exactly the heard total.
        NarrationTempos.Heard(tempo).ShouldBe(new NarrationPacing(sentenceMs, sceneMs));
        var added = NarrationTempos.Of(tempo);
        const int margins = NarrationGaps.TailKeepMs + NarrationGaps.LeadKeepMs;
        (added.SentenceGapMs + margins).ShouldBe(sentenceMs);
        (added.SceneGapMs + margins).ShouldBe(sceneMs);
        sceneMs.ShouldBeLessThanOrEqualTo(500, "no tempo waits more than half a second");
    }

    [Fact]
    public void ATemplateSavedBeforeTheTempoReadsAsNatural()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new VideoStyle { Name = "x" }, global::TMConcept.Core.Render.CoreJsonContext.Default.VideoStyle)
            .Replace("\"tempo\":1,", string.Empty, StringComparison.Ordinal).Replace(",\"tempo\":1", string.Empty, StringComparison.Ordinal)
            .Replace("\"Tempo\":1,", string.Empty, StringComparison.Ordinal).Replace(",\"Tempo\":1", string.Empty, StringComparison.Ordinal);
        json.ShouldNotContain("empo");
        System.Text.Json.JsonSerializer.Deserialize(json, global::TMConcept.Core.Render.CoreJsonContext.Default.VideoStyle)!.Tempo.ShouldBe(NarrationTempo.Natural);
    }

    [Fact]
    public void AnUnknownTempoCannotBeSaved() =>
        VideoStyleRules.Problems(new VideoStyle { Name = "x", Tempo = (NarrationTempo)9 }).ShouldContain(VideoStyleProblem.UnknownValue);
}
