namespace TMConcept.Core.Styles;

/// <summary>
/// A font the app ships for videos (SIL Open Font License 1.1, from github.com/google/fonts). The files live in the app's video font folder;
/// the render step copies them into the work folder, so the video code and the subtitles load them by <see cref="Family"/>.
/// </summary>
/// <param name="Id">Stable id stored in a template ("fira-sans").</param>
/// <param name="Family">The family name inside the font file (CSS <c>font-family</c>, ASS <c>Fontname</c>); also its display name.</param>
/// <param name="RegularFile">File name of the regular weight.</param>
/// <param name="BoldFile">File name of the bold weight; <c>null</c> when the font has a single weight (display faces).</param>
/// <param name="Licence">SPDX licence id.</param>
/// <param name="Source">Folder in github.com/google/fonts the files and the licence (OFL.txt) come from.</param>
/// <param name="Cyrillic">The font draws Cyrillic (checked in its character map); others fall back to <see cref="VideoFonts.CyrillicFallbackId"/>.</param>
public sealed record VideoFont(string Id, string Family, string RegularFile, string? BoldFile, string Licence, string Source, bool Cyrillic);
