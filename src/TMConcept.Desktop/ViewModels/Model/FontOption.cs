using Avalonia.Media;
using TMConcept.Core.Styles;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A video font in the template editor: its name, drawn in the font itself when the font file is there (<see cref="Videos.VideoFontPreview"/>).</summary>
public sealed record FontOption(VideoFont Font, FontFamily Preview);
