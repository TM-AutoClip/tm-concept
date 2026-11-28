using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Images;
using TMConcept.Core.Plans;
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
    public void TheStickFigureNarratorAdaptsTheScenesWhileTheVideoIsMadeAndOtherStylesDoNot()
    {
        // 28 Nov 2026: the concept is written for no style; the scene plan and the video code adapt the scenes to the chosen look.
        DrawStyles.Of(DrawStyle.StickFigure).SceneGuidance.ShouldContain("stick-figure narrator");
        DrawStyles.All.Where(d => d.Style != DrawStyle.StickFigure).ShouldAllBe(d => d.SceneGuidance.Length == 0, "only the storytelling look adapts the scenes");
        var narrator = new VideoStyle { Name = "t", DrawStyle = DrawStyle.StickFigure };
        var scenes = new[] { new PlanScene(1, 6, "a map of the route", ["The army marched."], string.Empty) };

        PlanPromptBuilder.User("Siege", "en", narrator, scenes).ShouldContain("Adapt the scenes to this look: " + DrawStyles.Of(DrawStyle.StickFigure).SceneGuidance);
        VideoCodePrompt.User(Input("stick-figure")).ShouldContain("Adapt the scenes to this look: " + DrawStyles.Of(DrawStyle.StickFigure).SceneGuidance);
        VideoStylePrompt.Block(TMConcept.Core.Engines.EngineKind.HyperFrames, narrator, "en").ShouldNotContain("Adapt the scenes",
            customMessage: "the block stays small for local models; the guidance goes with the scenes");
        foreach (var plain in new[] { DrawStyle.Auto, DrawStyle.Blueprint })
        {
            var style = new VideoStyle { Name = "t", DrawStyle = plain };
            PlanPromptBuilder.User("Siege", "en", style, scenes).ShouldNotContain("Adapt the scenes", customMessage: plain.ToString());
            VideoCodePrompt.User(Input(DrawStyles.Of(plain).Key)).ShouldNotContain("Adapt the scenes", customMessage: plain.ToString());
        }

        DrawStyles.SceneGuidance(null).ShouldBeEmpty("no snapshot, no look");
        DrawStyles.SceneGuidanceOfKey("stick-figure").ShouldBe(DrawStyles.Of(DrawStyle.StickFigure).SceneGuidance, "the same guidance by the render key");
        DrawStyles.SceneGuidanceOfKey(null).ShouldBeEmpty();
        DrawStyles.SceneGuidance(new VideoStyle { DrawStyle = DrawStyle.Auto, ChosenStyle = "ink wash" }).ShouldBeEmpty("the model's own words have no guidance");
    }

    private static RenderInput Input(string style)
    {
        var input = RenderInputBuilder.Build("Siege", "en", [new("a map", "The army marched.", string.Empty, 6000)], 1920, 1080, 30);
        input.Style = new RenderStyle { DrawStyle = style };
        return input;
    }

    [Fact]
    public void TheConceptAndEditPromptsHaveNoStyleLine()
    {
        var profile = new Profile { Name = "Kanal", ContentLanguage = ContentLanguages.Turkish, ConceptText = "Tarih" };
        foreach (var language in new[] { ContentLanguages.Turkish, ContentLanguages.English })
        {
            profile.ContentLanguage = language;
            var concepts = ConceptPromptBuilder.Build(new ConceptPromptInput(profile, PromptMemory.Empty, string.Empty, 3));
            var edit = ConceptPromptBuilder.BuildEdit(new ConceptEditPromptInput(profile, "Başlık", "Özet.", "[ekranda: harita]\nMetin.", "kısalt"));
            foreach (var text in new[] { concepts.System + concepts.User, edit.System + edit.User })
            {
                text.ShouldNotContain("ÇİZİM TARZI");
                text.ShouldNotContain("DRAWING STYLE");
                text.ShouldNotContain("{style}");
            }
        }
    }
}
