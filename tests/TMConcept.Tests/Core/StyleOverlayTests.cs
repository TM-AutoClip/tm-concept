using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Core.Scenes;
using TMConcept.Core.Styles;
using TMConcept.Core.Subtitles;

namespace TMConcept.Tests.Core;

/// <summary>
/// What the app draws over a template video (0.1.19): subtitles in the template's font and colours, the progress bar only when it is on, the
/// closing card with or without the channel photo; and the spoken closing added to the last scene.
/// </summary>
public sealed class StyleOverlayTests
{
    private static readonly IReadOnlyList<SubtitleCue> Cues = [new(0, 2000, ["In 672 the city was under siege."])];

    private static VideoStyle Style(bool subtitles = false, bool progressBar = false, ClosingPlacement closing = ClosingPlacement.Off) => new()
    {
        Name = "t",
        Subtitles = subtitles,
        ProgressBar = progressBar,
        FontId = "poppins",
        PaletteId = "ocean",
        Closing = closing,
        ClosingPhrase = ClosingPhrase.MoreVideos,
        ClosingPlatform = ClosingPlatform.YouTube,
    };

    private static StyleOverlayResult Build(VideoStyle style, string language = ContentLanguages.English, bool photo = false, string channel = "Byzantium") =>
        StyleOverlay.Build(1080, 1920, 20, Cues, style, language, VideoStyleRules.Closing(style, language), channel, photo);

    [Fact]
    public void NothingToDrawMeansNoOverlay()
    {
        var result = Build(Style());

        result.Ass.ShouldBeNull();
        result.Card.ShouldBeNull();
    }

    [Fact]
    public void TheProgressBarIsDrawnOnlyWhenItIsOn()
    {
        var on = Build(Style(progressBar: true)).Ass!;

        on.ShouldContain(@"\clip(0,0,0,12)\t(0,20000,\clip(0,0,1080,12))", customMessage: "the bar grows over the whole video, 0.6 % of the height");
        on.ShouldContain(StyleOverlay.Tag("#2EC4F1"), customMessage: "in the accent colour");
        on.ShouldNotContain("siege", customMessage: "subtitles off: no subtitle line");
        Build(Style(subtitles: true)).Ass!.ShouldNotContain(@"\clip");
    }

    [Fact]
    public void SubtitlesUseTheTemplateFontAndPalette()
    {
        var ass = Build(Style(subtitles: true)).Ass!;

        ass.ShouldContain("Style: Default,Poppins,59,&H00FFFBF1,");
        ass.ShouldContain("Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,In 672 the city was under siege.");
        Build(Style(subtitles: true), ContentLanguages.Russian).Ass!.ShouldContain("Style: Default,Fira Sans,", customMessage: "Cyrillic falls back");
    }

    [Fact]
    public void TheClosingCardShowsTheChannelThePhotoRingTheSentenceAndTheButton()
    {
        var result = Build(Style(closing: ClosingPlacement.Screen), ContentLanguages.Turkish, photo: true, channel: "Bizans {Tarihi}");

        var card = result.Card.ShouldNotBeNull();
        card.Photo.ShouldBeTrue();
        card.StartSeconds.ShouldBe(20 - StyleLayout.EndCardSeconds);
        card.EndSeconds.ShouldBe(20);
        card.PhotoSize.ShouldBe(324);
        (card.PhotoX + (card.PhotoSize / 2)).ShouldBe(540, "centred");
        var ass = result.Ass!;
        ass.ShouldContain("Bizans (Tarihi)", customMessage: "braces cannot start an override");
        ass.ShouldContain("Diğer videolar için abone olmayı unutma!");
        ass.ShouldContain("Abone ol");
        ass.ShouldContain(@"\fad(400,0)");
        ass.ShouldContain("Dialogue: 11,0:00:16.50,0:00:20.00,TmShape", customMessage: "the accent ring behind the photo");
        ass.ShouldNotContain(@"\clip", customMessage: "no progress bar");
    }

    [Fact]
    public void ASubtitleStillOnScreenEndsWhereTheCardStartsAndNoneStartsUnderIt()
    {
        IReadOnlyList<SubtitleCue> cues = [new(0, 2000, ["In 672 the city was under siege."]), new(15000, 17500, ["Late line."]), new(17000, 19000, ["Under the card."])];

        var ass = StyleOverlay.Build(1080, 1920, 20, cues, Style(subtitles: true, closing: ClosingPlacement.Screen), ContentLanguages.English,
            VideoStyleRules.Closing(Style(closing: ClosingPlacement.Screen), ContentLanguages.English), "c", photo: false).Ass!;

        ass.ShouldContain("Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,In 672 the city was under siege.");
        ass.ShouldContain("Dialogue: 0,0:00:15.00,0:00:16.50,Default,,0,0,0,,Late line.", customMessage: "cut where the card starts");
        ass.ShouldNotContain("Under the card.");
    }

    [Fact]
    public void TheClosingCardsHoldIsAddedToTheLastSceneOnlyWhenTheCardIsShown()
    {
        IReadOnlyList<TMConcept.Core.Render.RenderSceneSource> scenes = [new("a", "b", string.Empty, 4000), new("c", "d", string.Empty, 5000)];
        var plain = TMConcept.Core.Render.RenderInputBuilder.Build("t", "en", scenes, 1080, 1920, 30);
        var screen = Style(closing: ClosingPlacement.Screen);
        var held = TMConcept.Core.Render.RenderInputBuilder.Build("t", "en", scenes, 1080, 1920, 30,
            endHoldMs: VideoStyleRules.EndHoldMs(screen, ContentLanguages.English));

        VideoStyleRules.EndHoldMs(screen, ContentLanguages.English).ShouldBe(3500);
        VideoStyleRules.EndHoldMs(Style(closing: ClosingPlacement.Both), ContentLanguages.English).ShouldBe(3500);
        VideoStyleRules.EndHoldMs(Style(closing: ClosingPlacement.Voice), ContentLanguages.English).ShouldBe(0);
        VideoStyleRules.EndHoldMs(screen, "xx").ShouldBe(0, "no sentences, no card, no hold");
        VideoStyleRules.EndHoldMs(null, ContentLanguages.English).ShouldBe(0);
        held.Scenes[0].FrameCount.ShouldBe(plain.Scenes[0].FrameCount);
        held.Scenes[1].FrameCount.ShouldBe(plain.Scenes[1].FrameCount + 105, "3.5 s at 30 fps");
        var card = StyleOverlay.Card(1080, 1920, held.DurationSeconds, photo: false);
        card.StartSeconds.ShouldBe(plain.DurationSeconds, "the card covers only the hold");
    }

    [Fact]
    public void ASpokenClosingWithoutAVoiceGoesOnTheCard()
    {
        var both = Style(closing: ClosingPlacement.Both);

        VideoStyleRules.WithoutVoice(both).ShouldNotBeNull().Closing.ShouldBe(ClosingPlacement.Screen);
        both.Closing.ShouldBe(ClosingPlacement.Both, "the template itself is not changed");
        VideoStyleRules.WithoutVoice(Style(closing: ClosingPlacement.Voice))!.Closing.ShouldBe(ClosingPlacement.Screen);
        var off = Style();
        VideoStyleRules.WithoutVoice(off).ShouldBeSameAs(off);
        VideoStyleRules.WithoutVoice(null).ShouldBeNull();
    }

    [Fact]
    public void WithoutAPhotoTheCardHasNoRing()
    {
        var result = Build(Style(closing: ClosingPlacement.Both), photo: false);

        result.Card.ShouldNotBeNull().Photo.ShouldBeFalse();
        result.Ass!.ShouldNotContain("Dialogue: 11,");
        result.Ass!.ShouldContain("Don't forget to subscribe for more videos!");
        result.Ass!.ShouldContain("Subscribe");
    }

    [Fact]
    public void AVoiceOnlyClosingDrawsNoCard()
    {
        Build(Style(closing: ClosingPlacement.Voice)).Card.ShouldBeNull();
        StyleOverlay.Build(1080, 1920, 20, Cues, Style(closing: ClosingPlacement.Screen), "xx", null, "c", photo: true).Ass
            .ShouldBeNull("a language without sentences gets no closing");
    }

    [Fact]
    public void TheSpokenClosingEndsTheLastSceneOnceInTheVideosLanguage()
    {
        IReadOnlyList<Scene> scenes = [new(1, "a map", "In 672 the city was under siege."), new(2, "ships", "A strange fire") { Script = "A strange fire [pause 1 s]" }];
        var turkish = VideoStyleRules.Closing(Style(closing: ClosingPlacement.Voice), ContentLanguages.Turkish)!;
        var english = VideoStyleRules.Closing(Style(closing: ClosingPlacement.Both), ContentLanguages.English)!;

        var tr = ClosingNarration.Append(scenes, turkish.Sentence);
        var en = ClosingNarration.Append(scenes, english.Sentence);

        turkish.Voice.ShouldBeTrue();
        turkish.Screen.ShouldBeFalse();
        tr[0].ShouldBeSameAs(scenes[0]);
        tr[1].Narration.ShouldBe("A strange fire. Diğer videolar için abone olmayı unutma!");
        tr[1].Script.ShouldBe("A strange fire [pause 1 s] Diğer videolar için abone olmayı unutma!");
        en[1].Narration.ShouldBe("A strange fire. Don't forget to subscribe for more videos!");
        ClosingNarration.Append(en, english.Sentence).ShouldBeSameAs(en, "never added twice");
        ClosingNarration.Append(scenes, string.Empty).ShouldBeSameAs(scenes);
        VideoStyleRules.Closing(Style(closing: ClosingPlacement.Screen), ContentLanguages.English)!.Voice.ShouldBeFalse();
    }
}
