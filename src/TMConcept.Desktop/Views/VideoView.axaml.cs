using Avalonia.Controls;
using Avalonia.Input;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Views;

/// <summary>Concept → video page.</summary>
public sealed partial class VideoView : UserControl
{
    /// <summary>Creates.</summary>
    public VideoView() => InitializeComponent();

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is VideoViewModel page)
        {
            page.CloseModels();
            e.Handled = true;
        }
    }
}
