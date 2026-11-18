namespace TMConcept.Core.Styles;

/// <summary>
/// A font for videos: one the app ships (SIL Open Font License 1.1, from github.com/google/fonts; embedded in the program) or one installed on
/// this computer (<see cref="SystemFonts"/>, 0.1.24: its files are named by their full path and are never shipped). The render step copies
/// the files into the work folder, so the video code and the subtitles load them by <see cref="Family"/>.
/// </summary>
/// <param name="Id">Stable id stored in a template ("fira-sans"; "system:Times New Roman" for an installed font).</param>
/// <param name="Family">The family name inside the font file (CSS <c>font-family</c>, ASS <c>Fontname</c>); also its display name.</param>
/// <param name="RegularFile">File name of the regular weight (a full path for an installed font).</param>
/// <param name="BoldFile">File name of the bold weight; <c>null</c> when the font has a single weight (display faces).</param>
/// <param name="Licence">SPDX licence id ("system" for an installed font: its own licence, the file never leaves the computer).</param>
/// <param name="Source">Folder in github.com/google/fonts the files and the licence (OFL.txt) come from; the folder of an installed font.</param>
/// <param name="Cyrillic">The font draws Cyrillic (checked in its character map); others fall back to <see cref="VideoFonts.CyrillicFallbackId"/>.</param>
/// <param name="ItalicFile">File name of the italic; <c>null</c> when there is none (the renderer slants the regular one).</param>
/// <param name="BoldItalicFile">File name of the bold italic; <c>null</c> when there is none.</param>
public sealed record VideoFont(string Id, string Family, string RegularFile, string? BoldFile, string Licence, string Source, bool Cyrillic,
    string? ItalicFile = null, string? BoldItalicFile = null)
{
    /// <summary>A font installed on this computer, not shipped with the app.</summary>
    public bool Installed => Id.StartsWith(SystemFonts.IdPrefix, StringComparison.Ordinal);
}
