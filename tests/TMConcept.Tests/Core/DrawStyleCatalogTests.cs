using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Images;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;
using TMConcept.Engines.Engines;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.32: the drawing styles live in one catalog (description, image words, drawing, craft, texture, palette, concept guidance), a template
/// without a style lets the model choose, and a storytelling style changes how concepts are written.
/// </summary>
public sealed class DrawStyleCatalogTests
{
    [Fact]
    public void EveryStyleHasOneDefinitionWithItsWordsAndAKnownPalette()
    {
        foreach (var style in Enum.GetValues<DrawStyle>())
        {
            DrawStyles.All.Count(d => d.Style == style).ShouldBe(1, style.ToString());
            var definition = DrawStyles.Of(style);
            definition.Key.ShouldNotBeNullOrWhiteSpace();
            if (style != DrawStyle.Auto)
            {
                definition.ImagePrompt.ShouldNotBeNullOrWhiteSpace(style.ToString());
                definition.Drawing.ShouldNotBeNullOrWhiteSpace(style.ToString());
                definition.Craft.ShouldNotBeNullOrWhiteSpace(style.ToString());
            }

            if (definition.PaletteId.Length > 0)
            {
                VideoStyleRules.FindPalette(definition.PaletteId).ShouldNotBeNull(definition.PaletteId);
            }
        }

        DrawStyles.All.Select(d => d.Key).ShouldBeUnique();
        DrawStyles.All[0].Style.ShouldBe(DrawStyle.Auto, "the AI choice comes first in the editor");
        DrawStyles.FindKey("auto").ShouldBeNull();
        DrawStyles.FindKey(" Pencil-Sketch ")!.Style.ShouldBe(DrawStyle.PencilSketch);
        DrawStyles.Of((DrawStyle)99).Style.ShouldBe(DrawStyle.Flat);
    }

    [Fact]
    public void TheNewPalettesPassTheContrastCheck()
    {
        foreach (var id in new[] { "blueprint", "hologram", "terminal", "sketch" })
        {
            PaletteDesigner.Check(VideoStyleRules.FindPalette(id)!).Ok.ShouldBeTrue(id);
        }
    }

    [Fact]
    public void ANewTemplateLetsTheModelChooseAndTheValuesStayAsTheyWereStored()
    {
        new VideoStyle().DrawStyle.ShouldBe(DrawStyle.Auto);
        ((int)DrawStyle.Flat).ShouldBe(0);
        ((int)DrawStyle.Neon).ShouldBe(5, "stored as numbers: older values never move");
        ((int)DrawStyle.Auto).ShouldBe(13);
        ((int)DrawStyle.PixelArt).ShouldBe(14, "0.1.51: added at the end");
    }

    [Fact]
    public void TheCodePromptGetsTheStylesDrawingCraftAndTexture()
    {
        var look = VideoStylePrompt.Look(new VideoStyle { DrawStyle = DrawStyle.Blueprint });
        look.ShouldContain("white technical line drawings");
        look.ShouldContain("Craft: Lines draw on");
        look.ShouldContain("faint grid texture");

        VideoStylePrompt.Look(new VideoStyle { DrawStyle = DrawStyle.Flat }).ShouldNotContain("texture", Case.Insensitive);
        VideoStylePrompt.Look(new VideoStyle { DrawStyle = DrawStyle.Auto }).ShouldContain("choose ONE drawing style");
        VideoStylePrompt.Look(new VideoStyle { DrawStyle = DrawStyle.Auto, ChosenStyle = "woodcut print, black ink" })
            .ShouldContain("«woodcut print, black ink»");
    }

    [Fact]
    public void ATemplateWithoutAStyleAsksTheImagePromptStepToChooseOne()
    {
        var auto = ImagePromptBuilder.User("Byzantium", ["a map"], DrawStyle.Auto);
        auto.ShouldContain("No illustration style was chosen");
        auto.ShouldContain("pencil-sketch");
        auto.ShouldNotContain("auto,");
        ImagePromptBuilder.Schema(chooseStyle: true)["required"]!.ToJsonString().ShouldContain("style");
        ImagePromptBuilder.Schema()["required"]!.ToJsonString().ShouldNotContain("style");

        ImagePromptBuilder.ReadStyle("""{"prompts":[{"scene":1,"prompt":"p"}],"style":"blueprint"}""").Preset.ShouldBe(DrawStyle.Blueprint);
        var own = ImagePromptBuilder.ReadStyle("""{"prompts":[],"style":"woodcut print with bold black ink"}""");
        own.Preset.ShouldBeNull();
        own.Words.ShouldBe("woodcut print with bold black ink");
        ImagePromptBuilder.ReadStyle("""{"prompts":[]}""").Words.ShouldBeEmpty();
        ImagePromptBuilder.Version.ShouldBe("3", "kept prompts of the format before are made again");
    }

    [Fact]
    public void TheTextureIsTheAppsOwnLayerOnTheHyperFramesPage()
    {
        var input = RenderInputBuilder.Build("t", "en", [new("v", "c", "", 1000)], 1080, 1920, 30);
        RenderStyleBuilder.Apply(input, new VideoStyle { Name = "x", DrawStyle = DrawStyle.Terminal });
        input.Style!.Texture.ShouldBe("scanlines");
        input.Style.DrawStyle.ShouldBe("terminal");
        var page = HyperFramesEngine.IndexHtml(input);
        page.ShouldContain("<div id=\"tm-texture\"");
        page.ShouldContain("#tm-texture {");

        var plain = RenderInputBuilder.Build("t", "en", [new("v", "c", "", 1000)], 1080, 1920, 30);
        HyperFramesEngine.IndexHtml(plain).ShouldNotContain("tm-texture", customMessage: "a video without a template keeps its page");
        StyleTextureCss.Css("unknown").ShouldBeEmpty();
    }

    [Fact]
    public void TheStickFigureNarratorChangesHowConceptsAreWrittenAndOtherStylesAskForDrawableScenes()
    {
        DrawStyles.Of(DrawStyle.StickFigure).HasConceptGuidance.ShouldBeTrue();
        DrawStyles.ConceptLine(DrawStyle.StickFigure, ContentLanguages.Turkish).ShouldContain("çöp adam anlatıcı");
        DrawStyles.ConceptLine(DrawStyle.StickFigure, ContentLanguages.English).ShouldContain("stick-figure narrator");
        DrawStyles.ConceptLine(DrawStyle.Blueprint, ContentLanguages.Turkish).ShouldContain("Video şu tarzda çizilecek");
        DrawStyles.ConceptLine(DrawStyle.Auto, ContentLanguages.Turkish).ShouldBeEmpty();
        DrawStyles.ConceptLine(null, ContentLanguages.Turkish).ShouldBeEmpty();
    }

    [Fact]
    public void TheConceptAndEditPromptsCarryTheStyleLineOnlyWhenOneIsChosen()
    {
        var profile = new Profile { Name = "Kanal", ContentLanguage = ContentLanguages.Turkish, ConceptText = "Tarih" };
        var memory = PromptMemory.Empty;
        var with = ConceptPromptBuilder.Build(new ConceptPromptInput(profile, memory, string.Empty, 3,
            Style: DrawStyles.ConceptLine(DrawStyle.StickFigure, ContentLanguages.Turkish)));
        (with.System + with.User).ShouldContain("ÇİZİM TARZI");
        (with.System + with.User).ShouldContain("çöp adam anlatıcı");

        var without = ConceptPromptBuilder.Build(new ConceptPromptInput(profile, memory, string.Empty, 3));
        (without.System + without.User).ShouldNotContain("ÇİZİM TARZI");
        (without.System + without.User).ShouldNotContain("{style}");

        var edit = ConceptPromptBuilder.BuildEdit(new ConceptEditPromptInput(profile, "Başlık", "Özet.", "[ekranda: harita]\nMetin.", "uyarla",
            DrawStyles.ConceptLine(DrawStyle.StickFigure, ContentLanguages.Turkish)));
        (edit.System + edit.User).ShouldContain("ÇİZİM TARZI");
        var plainEdit = ConceptPromptBuilder.BuildEdit(new ConceptEditPromptInput(profile, "Başlık", "Özet.", "[ekranda: harita]\nMetin.", "kısalt"));
        (plainEdit.System + plainEdit.User).ShouldNotContain("{style}");
    }
}
