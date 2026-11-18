using System.Text.Json;
using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;

namespace TMConcept.Tests.Core;

/// <summary>
/// Templates (0.1.19): the shared tables (palettes, fonts, closing sentences), what a template resolves to, what can be saved, and the
/// snapshot a video takes when it starts (videos made before templates keep their old behaviour).
/// </summary>
public sealed class VideoStyleTests
{
    [Fact]
    public void TheDefaultPaletteIsTheLookBeforeTemplatesAndEveryPaletteIsComplete()
    {
        var old = new RenderTheme();
        var palette = VideoStyleRules.Palette(null);
        palette.Id.ShouldBe(VideoPalettes.DefaultId);
        VideoStyleRules.Theme(palette).ShouldBeEquivalentTo(old);
        VideoPalettes.All.Select(p => p.Id).ShouldBeUnique();
        foreach (var each in VideoPalettes.All)
        {
            foreach (var token in PaletteTokens.All)
            {
                PaletteColors.Value(each, token).ShouldMatch("^#[0-9A-F]{6}$");
            }
        }
    }

    [Fact]
    public void FontsAreOflWithAStaticFileAndRussianFallsBackToACyrillicFont()
    {
        VideoFonts.All.Select(f => f.Id).ShouldBeUnique();
        VideoFonts.All.Count.ShouldBeInRange(4, 8);
        VideoFonts.All.ShouldAllBe(f => f.Licence == "OFL-1.1" && f.RegularFile.EndsWith(".ttf") && !f.RegularFile.Contains('['));
        VideoStyleRules.FindFont(VideoFonts.CyrillicFallbackId)!.Cyrillic.ShouldBeTrue();

        var poppins = new VideoStyle { FontId = "poppins" };
        VideoStyleRules.Font(poppins, ContentLanguages.Turkish).Id.ShouldBe("poppins");
        VideoStyleRules.Font(poppins, ContentLanguages.Russian).Id.ShouldBe(VideoFonts.CyrillicFallbackId);
        VideoStyleRules.Font(new VideoStyle { FontId = "gone" }, ContentLanguages.English).Id.ShouldBe(VideoFonts.DefaultId);
        VideoStyleRules.Palette(new VideoStyle { PaletteId = "gone" }).Id.ShouldBe(VideoPalettes.DefaultId);
    }

    [Fact]
    public void EveryOutputLanguageHasEveryClosingSentenceInBothWordings()
    {
        foreach (var language in ContentLanguages.All)
        {
            var table = VideoStyleRules.ClosingFor(language).ShouldNotBeNull(language);
            table.SubscribeLabel.ShouldNotBeNullOrWhiteSpace();
            table.FollowLabel.ShouldNotBeNullOrWhiteSpace();
            table.Lines.Select(l => l.Phrase).ShouldBe(Enum.GetValues<ClosingPhrase>(), ignoreOrder: true);
            table.Lines.ShouldAllBe(l => l.Subscribe.Trim().Length > 0 && l.Follow.Trim().Length > 0);
        }

        ClosingPhrases.All.Select(l => l.Code).ShouldBeUnique();
    }

    [Fact]
    public void TheClosingFollowsThePlatformWordingAndThePlacement()
    {
        var style = new VideoStyle { Closing = ClosingPlacement.Voice, ClosingPhrase = ClosingPhrase.WhatsNext, ClosingPlatform = ClosingPlatform.YouTube };
        var youtube = VideoStyleRules.Closing(style, ContentLanguages.Turkish).ShouldNotBeNull();
        youtube.ShouldBe(new ClosingText("Devamı için abone olmayı unutma!", "Abone ol", Voice: true, Screen: false));

        style.ClosingPlatform = ClosingPlatform.Instagram;
        style.ClosingPhrase = ClosingPhrase.MoreVideos;
        style.Closing = ClosingPlacement.Both;
        VideoStyleRules.Closing(style, ContentLanguages.Turkish)
            .ShouldBe(new ClosingText("Diğer videolar için takip etmeyi unutma!", "Takip et", Voice: true, Screen: true));

        style.ClosingPlatform = ClosingPlatform.TikTok;
        style.Closing = ClosingPlacement.Screen;
        style.ClosingPhrase = ClosingPhrase.Short;
        VideoStyleRules.Closing(style, ContentLanguages.English).ShouldBe(new ClosingText("Don't forget to follow!", "Follow", Voice: false, Screen: true));
    }

    [Fact]
    public void NoClosingWhenOffOrWithoutATemplateAndAMissingLanguageIsToldApart()
    {
        VideoStyleRules.Closing(null, ContentLanguages.Turkish).ShouldBeNull();
        VideoStyleRules.Closing(new VideoStyle(), ContentLanguages.Turkish).ShouldBeNull();
        VideoStyleRules.ClosingMissing(new VideoStyle(), "ja").ShouldBeFalse();

        var on = new VideoStyle { Closing = ClosingPlacement.Both };
        VideoStyleRules.Closing(on, "ja").ShouldBeNull();
        VideoStyleRules.ClosingMissing(on, "ja").ShouldBeTrue();
        VideoStyleRules.ClosingMissing(on, ContentLanguages.English).ShouldBeFalse();
    }

    [Fact]
    public void ANewTemplateHasTheDecidedDefaults()
    {
        var style = new VideoStyle();
        style.ProgressBar.ShouldBeFalse();
        style.Subtitles.ShouldBeTrue();
        style.Closing.ShouldBe(ClosingPlacement.Off);
        style.PaletteId.ShouldBe(VideoPalettes.DefaultId);
        style.FontId.ShouldBe(VideoFonts.DefaultId);
        VideoStyleLimits.Max.ShouldBe(20);
    }

    [Fact]
    public void ProblemsNameEverythingThatCannotBeSaved()
    {
        VideoStyleRules.Problems(new VideoStyle { Name = "Tarih" }).ShouldBeEmpty();
        VideoStyleRules.Problems(new VideoStyle { Name = "  " }).ShouldBe([VideoStyleProblem.NameMissing]);
        VideoStyleRules.Problems(new VideoStyle { Name = new string('a', VideoStyleLimits.NameMax + 1) }).ShouldBe([VideoStyleProblem.NameTooLong]);
        VideoStyleRules.Problems(new VideoStyle { Name = "x", PaletteId = "gone", FontId = "gone", TitlePosition = (TitlePosition)9 })
            .ShouldBe([VideoStyleProblem.UnknownPalette, VideoStyleProblem.UnknownFont, VideoStyleProblem.UnknownValue]);
    }

    [Fact]
    public void ASnapshotIsACopyAndSetsTheOldSubtitlesField()
    {
        var style = new VideoStyle { Name = "Tarih", Subtitles = false, ProgressBar = true, PaletteId = "paper", Closing = ClosingPlacement.Both };
        var options = new VideoOptions { Subtitles = true };

        VideoStyleRules.Snapshot(options, style);
        style.Name = "Changed after start";
        style.Subtitles = true;

        options.Style.ShouldNotBeNull().Name.ShouldBe("Tarih");
        options.Style.ShouldNotBeSameAs(style);
        options.Subtitles.ShouldBeFalse();
        VideoStyleRules.SubtitlesOn(options).ShouldBeFalse();
        VideoStyleRules.ProgressBarOn(options).ShouldBeTrue();
    }

    [Fact]
    public void AVideoWithoutASnapshotBehavesAsBefore()
    {
        var old = JsonSerializer.Deserialize("""{"engine":0,"codeProvider":"p","codeModel":"m","images":"none","speech":true,"subtitles":false}""",
            CoreJsonContext.Default.VideoOptions).ShouldNotBeNull();
        old.Style.ShouldBeNull();
        VideoStyleRules.SubtitlesOn(old).ShouldBeFalse();
        VideoStyleRules.ProgressBarOn(old).ShouldBeFalse();
        VideoStyleRules.SubtitlesOn(new VideoOptions()).ShouldBeTrue();
    }

    [Fact]
    public void TheSnapshotSurvivesTheStoredJson()
    {
        var options = new VideoOptions();
        VideoStyleRules.Snapshot(options, new VideoStyle
        {
            Name = "Neon", FontId = "anton", TitleStyle = TitleStyle.Boxed, TitlePosition = TitlePosition.BottomRight, DrawStyle = DrawStyle.Neon,
            Motion = MotionIntensity.Lively, Closing = ClosingPlacement.Screen, ClosingPhrase = ClosingPhrase.Thanks, ClosingPlatform = ClosingPlatform.TikTok,
        });

        var json = JsonSerializer.Serialize(options, CoreJsonContext.Default.VideoOptions);
        var back = JsonSerializer.Deserialize(json, CoreJsonContext.Default.VideoOptions).ShouldNotBeNull();
        JsonSerializer.Serialize(back, CoreJsonContext.Default.VideoOptions).ShouldBe(json);
        back.Style.ShouldNotBeNull().TitlePosition.ShouldBe(TitlePosition.BottomRight);
    }

    [Fact]
    public void TitlePositionsAreTheNineCellsOfTheGrid()
    {
        foreach (var position in Enum.GetValues<TitlePosition>())
        {
            var anchor = TitleAnchors.Of(position);
            TitleAnchors.At(anchor.Row, anchor.Column).ShouldBe(position);
        }

        Enum.GetValues<TitlePosition>().Length.ShouldBe(9);
        TitleAnchors.Of(TitlePosition.TopLeft).ShouldBe(new TitleAnchor(0, 0, 0, 0));
        TitleAnchors.Of(TitlePosition.MiddleCenter).ShouldBe(new TitleAnchor(0.5, 0.5, 1, 1));
        TitleAnchors.Of(TitlePosition.BottomRight).ShouldBe(new TitleAnchor(1, 1, 2, 2));
        Should.Throw<ArgumentOutOfRangeException>(() => TitleAnchors.Of((TitlePosition)9));
        Should.Throw<ArgumentOutOfRangeException>(() => TitleAnchors.At(3, 0));
    }
}
