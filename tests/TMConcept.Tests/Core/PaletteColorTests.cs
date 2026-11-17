using Shouldly;
using TMConcept.Core.Styles;

namespace TMConcept.Tests.Core;

/// <summary>Colour literals the video code writes and their nearest palette token (the style check's free auto-fix, 0.1.19).</summary>
public sealed class PaletteColorTests
{
    private static readonly VideoPalette Navy = VideoStyleRules.Palette(null);

    [Theory]
    [InlineData("#fb2", 255, 187, 34, 1.0)]
    [InlineData("#FFB224", 255, 178, 36, 1.0)]
    [InlineData(" #ffb22480 ", 255, 178, 36, 128 / 255.0)]
    [InlineData("#0008", 0, 0, 0, 136 / 255.0)]
    [InlineData("rgb(255, 178, 36)", 255, 178, 36, 1.0)]
    [InlineData("RGBA(255,178,36,0.5)", 255, 178, 36, 0.5)]
    [InlineData("rgb(255 178 36 / 25%)", 255, 178, 36, 0.25)]
    [InlineData("rgb(100%, 0%, 50%)", 255, 0, 128, 1.0)]
    public void ReadsTheColourLiterals(string literal, int r, int g, int b, double a)
    {
        PaletteColors.TryParse(literal, out var color).ShouldBeTrue();
        color.R.ShouldBe((byte)r);
        color.G.ShouldBe((byte)g);
        color.B.ShouldBe((byte)b);
        color.A.ShouldBe(a, 0.0001);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12")]
    [InlineData("#12345g")]
    [InlineData("hsl(10, 50%, 50%)")]
    [InlineData("rgb(256, 0, 0)")]
    [InlineData("rgb(1, 2)")]
    [InlineData("rgb(1 2 3 0.5)")]
    [InlineData("rgba(1, 2, 3, 2)")]
    [InlineData("rgb(1, 2 / 3)")]
    public void LeavesEverythingElse(string? literal) => PaletteColors.TryParse(literal, out _).ShouldBeFalse();

    [Theory]
    [InlineData("#FFA500", PaletteTokens.Accent)]
    [InlineData("#000000", PaletteTokens.Background)]
    [InlineData("#EEEEEE", PaletteTokens.Text)]
    [InlineData("#29B6F6", PaletteTokens.Accent2)]
    [InlineData("#1A2B55", PaletteTokens.Surface)]
    [InlineData("#9AA3B5", PaletteTokens.Muted)]
    public void FindsTheNearestToken(string literal, string token)
    {
        PaletteColors.TryParse(literal, out var color).ShouldBeTrue();
        PaletteColors.Nearest(Navy, color).ShouldBe(token);
    }

    [Fact]
    public void KnowsThePaletteColoursAndTheirCssVariables()
    {
        PaletteColors.TryParse("#ffb224", out var accent).ShouldBeTrue();
        PaletteColors.InPalette(Navy, accent).ShouldBeTrue();
        PaletteColors.TryParse("#ffb225", out var near).ShouldBeTrue();
        PaletteColors.InPalette(Navy, near).ShouldBeFalse();
        PaletteColors.CssVariable(PaletteTokens.Accent2).ShouldBe("var(--tm-accent2)");
        Should.Throw<ArgumentException>(() => PaletteColors.CssVariable("primary"));
        Should.Throw<ArgumentException>(() => PaletteColors.Value(Navy, "primary"));
    }
}
