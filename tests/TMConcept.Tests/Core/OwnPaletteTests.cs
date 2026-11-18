using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;
using TMConcept.Core.Subtitles;

namespace TMConcept.Tests.Core;

/// <summary>
/// The user's own colours (0.1.24): typed or made from one base colour, checked for contrast (unreadable text cannot be saved), copied into
/// the template so the render uses exactly them, and a template with broken own colours falls back to the default palette.
/// </summary>
public sealed class OwnPaletteTests
{
    [Theory]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("0b1633", "#0B1633")]
    [InlineData(" #FfB224 ", "#FFB224")]
    [InlineData("#12345", null)]
    [InlineData("red", null)]
    [InlineData("", null)]
    public void ColoursAreStoredAsRrggbb(string typed, string? stored) => PaletteDesigner.Normalize(typed).ShouldBe(stored);

    [Fact]
    public void UnreadableTextIsAnErrorAndAWeakAccentOnlyAWarning()
    {
        var grey = PaletteDesigner.Own("#777777", "#7A7A7A", "#7B7B7B", "#FFFFFF", "#888888", "#808080");
        var check = PaletteDesigner.Check(grey);
        check.Ok.ShouldBeFalse();
        check.Errors.ShouldBe([PaletteIssue.TextOnBackground, PaletteIssue.TextOnSurface]);
        check.Warnings.ShouldBe([PaletteIssue.AccentOnBackground, PaletteIssue.MutedOnBackground]);

        var weakAccent = PaletteDesigner.Own("#0B1633", "#16244A", "#1C2A55", "#4FC3F7", "#FFFFFF", "#A9B4CC");
        var second = PaletteDesigner.Check(weakAccent);
        second.Ok.ShouldBeTrue();
        second.Warnings.ShouldBe([PaletteIssue.AccentOnBackground]);
        second.TextContrast.ShouldBeGreaterThan(15);

        PaletteDesigner.Check(new VideoPalette(VideoPalettes.CustomId, "#000", "#111111", "#222222", "#333333", "#FFFFFF", "#999999", false))
            .Errors.ShouldBe([PaletteIssue.InvalidColour], "only the #RRGGBB form is stored");
    }

    [Theory]
    [InlineData("#2E86DE", false)]
    [InlineData("#2E86DE", true)]
    [InlineData("#E5383B", false)]
    [InlineData("#F2C94C", true)]
    [InlineData("#777777", false)]
    [InlineData("#000000", true)]
    [InlineData("#FFFFFF", false)]
    public void APaletteFromOneBaseColourAlwaysPassesTheCheck(string baseColour, bool light)
    {
        var palette = PaletteDesigner.FromBase(baseColour, light);

        var check = PaletteDesigner.Check(palette);
        check.Ok.ShouldBeTrue();
        check.Warnings.ShouldBeEmpty();
        palette.Light.ShouldBe(light);
        palette.Id.ShouldBe(VideoPalettes.CustomId);
        PaletteColors.Luminance(Color(palette.Background)).ShouldBe(light ? 0.85 : 0.01, light ? 0.15 : 0.02);
    }

    [Fact]
    public void TheBaseHueCarriesIntoTheAccent()
    {
        var palette = PaletteDesigner.FromBase("#E5383B", light: false);
        var accent = Color(palette.Accent);

        (accent.R > accent.G && accent.R > accent.B).ShouldBeTrue("a red base gives a red accent");
    }

    [Fact]
    public void ATemplateRendersWithItsOwnColours()
    {
        var own = PaletteDesigner.FromBase("#2E86DE", light: false);
        var style = new VideoStyle { Name = "t", PaletteId = VideoPalettes.CustomId, CustomPalette = own, Subtitles = true };

        VideoStyleRules.Problems(style).ShouldBeEmpty();
        VideoStyleRules.Palette(style).ShouldBe(own);
        RenderStyleBuilder.Tokens(style, ContentLanguages.English).Colors.Background.ShouldBe(own.Background);
        StyleOverlay.Build(1080, 1920, 5, [new SubtitleCue(0, 1000, ["x"])], style, ContentLanguages.English, null, "c", false).Ass!
            .ShouldContain("Style: Default,Fira Sans,59," + SubtitleBuilder.AssColour(own.Text) + ",");
        VideoStyleCheck.Check("el.style.color = '#123456';", style, ContentLanguages.English).Fixes.ShouldHaveSingleItem()
            .ShouldEndWith("(" + PaletteColors.Nearest(own, Color("#123456")) + ")", Case.Sensitive, "a stray colour goes to the nearest own colour");
    }

    [Fact]
    public void BrokenOwnColoursCannotBeSavedAndRenderWithTheDefaultPalette()
    {
        var unreadable = new VideoStyle
        {
            Name = "t", PaletteId = VideoPalettes.CustomId,
            CustomPalette = PaletteDesigner.Own("#777777", "#7A7A7A", "#7B7B7B", "#FFFFFF", "#888888", "#808080"),
        };
        var missing = new VideoStyle { Name = "t", PaletteId = VideoPalettes.CustomId };

        VideoStyleRules.Problems(unreadable).ShouldBe([VideoStyleProblem.PaletteContrast]);
        VideoStyleRules.Problems(missing).ShouldBe([VideoStyleProblem.UnknownPalette]);
        VideoStyleRules.Palette(unreadable).ShouldBe(VideoPalettes.All[0]);
        VideoStyleRules.Palette(missing).ShouldBe(VideoPalettes.All[0]);
    }

    private static ColorValue Color(string hex) => PaletteColors.TryParse(hex, out var c) ? c : throw new ArgumentException(hex);
}
