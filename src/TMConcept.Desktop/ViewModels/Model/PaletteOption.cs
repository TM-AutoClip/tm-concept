using Avalonia.Media;
using TMConcept.Core.Styles;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A colour palette in the template editor: its name and one swatch per token (background, surface, accent, accent 2, text, muted).</summary>
public sealed record PaletteOption(VideoPalette Palette, string Label, IReadOnlyList<IBrush> Swatches);
