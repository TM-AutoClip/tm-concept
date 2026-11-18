namespace TMConcept.Core.Styles;

/// <summary>
/// The fonts installed on this computer that a template may use (0.1.24: the developer asked for Times New Roman and other classic faces;
/// a Microsoft font may not be shipped, so it is offered from the computer instead). Only single TrueType / OpenType files (.ttf, .otf) with
/// the Turkish letters (Latin Extended-A) and a regular face; the bold, italic and bold italic faces of the family are found next to it. The
/// files never leave the computer: a video made with one copies them into its work folder only, like the shipped fonts. The list is read
/// once per run of the app; a template whose installed font is gone falls back to the default font with a warning
/// (<see cref="VideoStyleRules.FontMissing"/>).
/// </summary>
public static class SystemFonts
{
    /// <summary>The start of an installed font's id ("system:Times New Roman").</summary>
    public const string IdPrefix = "system:";

    private static readonly Lazy<IReadOnlyList<VideoFont>> Fonts = new(() => Scan(Folders()));

    /// <summary>The installed fonts, by family name (read on first use; an empty list on a computer without font folders).</summary>
    public static IReadOnlyList<VideoFont> Installed => Fonts.Value;

    /// <summary>The id of an installed family.</summary>
    public static string Id(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        return IdPrefix + family.Trim();
    }

    /// <summary>An installed font by id; <c>null</c> when the id is not an installed font's or the font is not on this computer.</summary>
    public static VideoFont? Find(string? id) =>
        id is not null && id.StartsWith(IdPrefix, StringComparison.Ordinal) ? Installed.FirstOrDefault(f => f.Id == id) : null;

    /// <summary>
    /// The usable families in <paramref name="folders"/> (searched with their sub folders; a missing folder is skipped). A family the app
    /// ships itself is left out (the shipped one is the same font).
    /// </summary>
    public static IReadOnlyList<VideoFont> Scan(IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var shipped = VideoFonts.All.Select(f => f.Family).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = new List<FontFileInfo>();
        foreach (var folder in folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                         .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".ttf" or ".otf"))
            {
                if (FontFileReader.Read(file) is { LatinExtended: true } info && !shipped.Contains(info.Family))
                {
                    files.Add(info);
                }
            }
        }

        var fonts = new List<VideoFont>();
        foreach (var family in files.GroupBy(f => f.Family, StringComparer.OrdinalIgnoreCase))
        {
            var upright = family.Where(f => !f.Italic).ToList();
            var slanted = family.Where(f => f.Italic).ToList();
            if (Closest(upright, 400, f => f.Weight < 600) is not { } regular)
            {
                continue;
            }

            var bold = Closest(upright, 700, f => f.Weight >= 600);
            var italic = Closest(slanted, 400, f => f.Weight < 600);
            var boldItalic = Closest(slanted, 700, f => f.Weight >= 600);
            fonts.Add(new VideoFont(Id(regular.Family), regular.Family, regular.Path, bold?.Path, "system", Path.GetDirectoryName(regular.Path) ?? string.Empty,
                family.Any(f => f.Cyrillic), italic?.Path, boldItalic?.Path));
        }

        return fonts.OrderBy(f => f.Family, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>The font folders of this system: Windows' own and the user's, the usual Linux and macOS folders.</summary>
    public static IReadOnlyList<string> Folders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            return
            [
                Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"),
            ];
        }

        return OperatingSystem.IsMacOS()
            ? ["/System/Library/Fonts", "/Library/Fonts", Path.Combine(home, "Library", "Fonts")]
            : ["/usr/share/fonts", "/usr/local/share/fonts", Path.Combine(home, ".local", "share", "fonts"), Path.Combine(home, ".fonts")];
    }

    /// <summary>The face whose weight is nearest <paramref name="weight"/> among those that pass <paramref name="fits"/> (ties: the smaller path).</summary>
    private static FontFileInfo? Closest(List<FontFileInfo> faces, int weight, Func<FontFileInfo, bool> fits) =>
        faces.Where(fits).OrderBy(f => Math.Abs(f.Weight - weight)).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
}
