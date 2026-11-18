using Shouldly;
using TMConcept.Core.Engines;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;
using TMConcept.Core.Subtitles;
using TMConcept.Core.Videos;
using TMConcept.Engines.Engines;

namespace TMConcept.Tests.Core;

/// <summary>
/// The wider font choice of 0.1.24: Tinos (a serif with Times New Roman's widths, OFL) and the italics of the shipped fonts, the weight and
/// slant of the text (bold, italic, bold italic) carried to the subtitles and every engine, and the fonts installed on the computer (read
/// from their own files, never shipped; a template whose installed font is gone falls back with a warning).
/// </summary>
public sealed class VideoFontChoiceTests : IDisposable
{
    private static readonly IReadOnlyList<SubtitleCue> Cues = [new(0, 2000, ["Rome."])];
    private readonly string _folder = Path.Combine(TestFolders.Root, "tmc-fonts-" + Guid.NewGuid().ToString("N"));

    public VideoFontChoiceTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static string RepositoryFile(params string[] parts)
    {
        var folder = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(folder, "TMConcept.slnx")))
        {
            folder = Path.GetDirectoryName(folder) ?? throw new InvalidOperationException("The repository root was not found.");
        }

        return Path.Combine([folder, .. parts]);
    }

    [Fact]
    public void TinosShipsWithAllFourFacesAndCyrillic()
    {
        var tinos = VideoStyleRules.FindFont("tinos").ShouldNotBeNull();

        tinos.Family.ShouldBe("Tinos");
        VideoFontFiles.Files(tinos).ShouldBe(["Tinos-Regular.ttf", "Tinos-Bold.ttf", "Tinos-Italic.ttf", "Tinos-BoldItalic.ttf"]);
        VideoStyleRules.Font(new VideoStyle { FontId = "tinos" }, ContentLanguages.Russian).Id.ShouldBe("tinos", "Tinos draws Cyrillic itself");
        foreach (var file in VideoFontFiles.Files(tinos))
        {
            using var stream = VideoFontFiles.Open(file);
            var info = FontFileReader.Read(stream, file).ShouldNotBeNull(file);
            info.Family.ShouldBe("Tinos");
            info.Italic.ShouldBe(file.Contains("Italic", StringComparison.Ordinal), file);
            (info.Weight >= 600).ShouldBe(file.Contains("Bold", StringComparison.Ordinal), file);
            (info.LatinExtended, info.Cyrillic).ShouldBe((true, true), file);
        }
    }

    [Fact]
    public void TheReaderAgreesWithSkiaOnTheShippedFonts()
    {
        foreach (var file in VideoFonts.All.SelectMany(VideoFontFiles.Files))
        {
            using var stream = VideoFontFiles.Open(file);
            var info = FontFileReader.Read(stream, file).ShouldNotBeNull(file);
            stream.Position = 0;
            using var typeface = SkiaSharp.SKTypeface.FromStream(stream);
            info.Family.ShouldBe(typeface.FamilyName, file);
            info.Italic.ShouldBe(typeface.IsItalic, file);
        }
    }

    [Fact]
    public void ANonFontIsNotRead()
    {
        var file = Path.Combine(_folder, "not-a-font.ttf");
        File.WriteAllText(file, "hello");

        FontFileReader.Read(file).ShouldBeNull();
        FontFileReader.Read(Path.Combine(_folder, "missing.ttf")).ShouldBeNull();
    }

    [Fact]
    public void InstalledFamiliesAreGroupedWithTheirFacesAndShippedOnesLeftOut()
    {
        // The interface's own fonts (not video fonts) stand in for an installed family; a shipped family in the folder is left out.
        var fonts = Path.Combine(_folder, "fonts", "sub");
        Directory.CreateDirectory(fonts);
        foreach (var name in new[] { "manrope-regular.ttf", "manrope-bold.ttf", "manrope-semibold.ttf" })
        {
            File.Copy(RepositoryFile("src", "TMConcept.Desktop", "Assets", "Fonts", name), Path.Combine(fonts, name));
        }

        using (var tinos = VideoFontFiles.Open("Tinos-Regular.ttf"))
        using (var copy = File.Create(Path.Combine(fonts, "Tinos-Regular.ttf")))
        {
            tinos.CopyTo(copy);
        }

        File.WriteAllText(Path.Combine(fonts, "broken.ttf"), "x");

        var found = SystemFonts.Scan([Path.Combine(_folder, "fonts"), Path.Combine(_folder, "missing")]);

        var manrope = found.ShouldHaveSingleItem();
        manrope.Family.ShouldBe("Manrope");
        manrope.Id.ShouldBe("system:Manrope");
        manrope.Installed.ShouldBeTrue();
        Path.GetFileName(manrope.RegularFile).ShouldBe("manrope-regular.ttf");
        Path.GetFileName(manrope.BoldFile).ShouldBe("manrope-bold.ttf", "the face nearest 700 is the bold one");
        manrope.ItalicFile.ShouldBeNull();
        manrope.Licence.ShouldBe("system");

        // Copied into a project by file name; the page names it by its folder-free path.
        var project = Path.Combine(_folder, "project");
        VideoFontFiles.CopyTo(manrope, project);
        Directory.GetFiles(project).Select(Path.GetFileName).ShouldBe(["manrope-regular.ttf", "manrope-bold.ttf"], ignoreOrder: true);
        RenderStyleBuilder.Faces(manrope).Select(f => f.File).ShouldBe(["fonts/manrope-regular.ttf", "fonts/manrope-bold.ttf"]);
    }

    [Fact]
    public void TimesNewRomanIsOfferedFromAWindowsComputer()
    {
        var times = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "times.ttf");
        if (!OperatingSystem.IsWindows() || !File.Exists(times))
        {
            return;
        }

        var font = SystemFonts.Find(SystemFonts.Id("Times New Roman")).ShouldNotBeNull();
        (font.BoldFile, font.ItalicFile, font.BoldItalicFile).ShouldNotBe((null, null, null));
        font.ItalicFile.ShouldNotBeNull();
        font.BoldItalicFile.ShouldNotBeNull();
        VideoStyleRules.Problems(new VideoStyle { Name = "t", FontId = font.Id }).ShouldBeEmpty();
    }

    [Fact]
    public void AMissingInstalledFontFallsBackAndCannotBeSaved()
    {
        var style = new VideoStyle { Name = "t", FontId = "system:No Such Family 12345" };

        VideoStyleRules.FontMissing(style).ShouldBeTrue();
        VideoStyleRules.Font(style, ContentLanguages.English).Id.ShouldBe(VideoFonts.DefaultId);
        VideoStyleRules.Problems(style).ShouldContain(VideoStyleProblem.UnknownFont);
        VideoStyleRules.FontMissing(new VideoStyle { FontId = "tinos" }).ShouldBeFalse();
        VideoStyleRules.FontMissing(new VideoStyle { FontId = "gone" }).ShouldBeFalse("an unknown shipped id is the old fallback, not an installed font");
    }

    [Theory]
    [InlineData(FontEmphasis.Auto, 400, 700, "normal", "1,0,0,0")]
    [InlineData(FontEmphasis.Regular, 400, 400, "normal", "0,0,0,0")]
    [InlineData(FontEmphasis.Bold, 700, 700, "normal", "1,0,0,0")]
    [InlineData(FontEmphasis.Italic, 400, 400, "italic", "0,1,0,0")]
    [InlineData(FontEmphasis.BoldItalic, 700, 700, "italic", "1,1,0,0")]
    public void TheEmphasisReachesTheTokensAndTheSubtitles(FontEmphasis emphasis, int text, int title, string slant, string assFlags)
    {
        var style = new VideoStyle { Name = "t", FontId = "tinos", FontEmphasis = emphasis, Subtitles = true };

        var tokens = RenderStyleBuilder.Tokens(style, ContentLanguages.English);
        (tokens.TextWeight, tokens.TitleWeight, tokens.TextStyle).ShouldBe((text, title, slant));
        var ass = StyleOverlay.Build(1080, 1920, 5, Cues, style, ContentLanguages.English, null, "c", false).Ass!;
        ass.ShouldContain(",&H66000000," + assFlags + ",100,100,");
    }

    [Fact]
    public void ThePageLoadsEveryFaceAndUsesTheChosenWeightAndSlant()
    {
        var input = SampleRender.Input();
        RenderStyleBuilder.Apply(input, new VideoStyle { Name = "t", FontId = "tinos", FontEmphasis = FontEmphasis.BoldItalic });

        input.Style!.Faces.Select(f => (f.File, f.Weight, f.Style)).ShouldBe(
        [
            ("fonts/Tinos-Regular.ttf", "400", "normal"), ("fonts/Tinos-Bold.ttf", "700", "normal"),
            ("fonts/Tinos-Italic.ttf", "400", "italic"), ("fonts/Tinos-BoldItalic.ttf", "700", "italic"),
        ]);
        var page = HyperFramesEngine.IndexHtml(input);
        page.ShouldContain("src: url(\"fonts/Tinos-BoldItalic.ttf\") format(\"truetype\"); font-weight: 700; font-style: italic;");
        page.ShouldContain("font-weight: 700; font-style: italic; }");
        page.ShouldContain(".tm-title { font-family: var(--tm-font); font-weight: 700; font-style: italic;");
        RevideoEngine.FontLoader(input).ShouldContain("['fonts/Tinos-Italic.ttf', '400', 'italic', 'truetype']");
        VideoStylePrompt.Block(EngineKind.HyperFrames, new VideoStyle { Name = "t", FontId = "tinos", FontEmphasis = FontEmphasis.Italic },
            ContentLanguages.English).ShouldContain("- Font: ONLY input.style.font (\"Tinos\"), weight 400 (titles 400), italic;");
    }

    [Fact]
    public void ASingleWeightFontClaimsEveryWeightAndAStyleFromBeforeKeepsItsFaces()
    {
        RenderStyleBuilder.Faces(VideoStyleRules.FindFont("anton")!).ShouldHaveSingleItem().Weight.ShouldBe("100 900");

        var old = new RenderStyle { FontRegular = "fonts/Anton-Regular.ttf" };
        RenderStyleBuilder.FacesOf(old).ShouldHaveSingleItem().Weight.ShouldBe("100 900");
        old.FontBold = "fonts/X-Bold.ttf";
        RenderStyleBuilder.FacesOf(old).Select(f => f.Weight).ShouldBe(["400", "700"]);
    }
}
