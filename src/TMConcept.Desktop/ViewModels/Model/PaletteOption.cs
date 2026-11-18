using Avalonia.Media;
using TMConcept.Core.Styles;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// A colour palette in the template editor: its name and one swatch per token (background, surface, accent, accent 2, text, muted). A saved
/// palette of the user carries its id (<paramref name="SavedId"/>); the template's own colours are the <paramref name="Own"/> entry.
/// </summary>
public sealed record PaletteOption(VideoPalette Palette, string Label, IReadOnlyList<IBrush> Swatches, Guid? SavedId = null, bool Own = false)
{
    /// <summary>The palette's colours are edited in the form (the own colours or a saved palette).</summary>
    public bool Editable => Own || SavedId is not null;
}
