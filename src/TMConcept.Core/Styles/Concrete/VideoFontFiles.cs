using System.Reflection;

namespace TMConcept.Core.Styles;

/// <summary>
/// The font files of <see cref="VideoFonts"/>, embedded in the program (so the single-file app needs no extra folder). The render step copies
/// the video's font into the engine project (<c>fonts/</c> and <c>public/fonts/</c>) and next to the subtitles, where the code and libass load
/// it by its family name. The licence texts ship next to the program (<c>licenses/font-&lt;name&gt;.txt</c>).
/// </summary>
public static class VideoFontFiles
{
    /// <summary>The folder name the fonts are copied into (inside a project and the work folder).</summary>
    public const string Folder = "fonts";

    private const string Prefix = "TMConcept.Fonts.";

    /// <summary>The files of a font: the regular weight, then the bold, italic and bold italic ones it has.</summary>
    public static IReadOnlyList<string> Files(VideoFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        return new[] { font.RegularFile, font.BoldFile, font.ItalicFile, font.BoldItalicFile }.OfType<string>().ToList();
    }

    /// <summary>The name a file has in the copied folder (an installed font's full path loses its folder).</summary>
    public static string Name(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        return Path.GetFileName(file);
    }

    /// <summary>The CSS <c>format()</c> of a font file: "opentype" for .otf, else "truetype".</summary>
    public static string Format(string file) =>
        string.Equals(Path.GetExtension(file), ".otf", StringComparison.OrdinalIgnoreCase) ? "opentype" : "truetype";

    /// <summary>Opens a font file; <see cref="InvalidOperationException"/> when the program does not carry it (a broken build).</summary>
    public static Stream Open(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return typeof(VideoFontFiles).Assembly.GetManifestResourceStream(Prefix + fileName)
            ?? throw new InvalidOperationException($"The video font '{fileName}' is missing from the program.");
    }

    /// <summary>Whether the program carries the file.</summary>
    public static bool Exists(string fileName) =>
        typeof(VideoFontFiles).Assembly.GetManifestResourceInfo(Prefix + fileName) is not null;

    /// <summary>
    /// Writes the font's files into <paramref name="folder"/> (created when missing; a file of the same size is left as it is). A shipped font
    /// comes from the program, an installed one from its own file on this computer (<see cref="FileNotFoundException"/> when it is gone).
    /// </summary>
    public static void CopyTo(VideoFont font, string folder)
    {
        ArgumentNullException.ThrowIfNull(font);
        Directory.CreateDirectory(folder);
        foreach (var name in Files(font))
        {
            var target = Path.Combine(folder, Name(name));
            using var source = font.Installed ? File.OpenRead(name) : Open(name);
            if (File.Exists(target) && new FileInfo(target).Length == source.Length)
            {
                continue;
            }

            using var output = File.Create(target);
            source.CopyTo(output);
        }
    }

    /// <summary>The embedded file names (tests check every font of <see cref="VideoFonts"/> is there).</summary>
    internal static IEnumerable<string> Embedded() =>
        Assembly.GetAssembly(typeof(VideoFontFiles))!.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(n => n[Prefix.Length..]);
}
