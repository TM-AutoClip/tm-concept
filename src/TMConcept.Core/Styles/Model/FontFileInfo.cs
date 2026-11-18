namespace TMConcept.Core.Styles;

/// <summary>What a font file says about itself (<see cref="FontFileReader"/>).</summary>
/// <param name="Path">The file.</param>
/// <param name="Family">Family name (the typographic one when the file has it, else the Windows family name).</param>
/// <param name="Weight">Weight class (400 regular, 700 bold).</param>
/// <param name="Italic">Italic or oblique.</param>
/// <param name="LatinExtended">It covers Latin Extended-A (ğ ş ı İ: Turkish and the other Latin languages of the app).</param>
/// <param name="Cyrillic">It covers Cyrillic.</param>
public sealed record FontFileInfo(string Path, string Family, int Weight, bool Italic, bool LatinExtended, bool Cyrillic);
