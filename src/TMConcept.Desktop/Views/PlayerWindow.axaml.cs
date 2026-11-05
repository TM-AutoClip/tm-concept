using Avalonia.Controls;
using Avalonia.Input;

namespace TMConcept.Desktop.Views;

/// <summary>
/// The player in full screen (the same player as the page: position and sound go on). Esc closes it; ⛶ closes it through the page's
/// player view, which opened it.
/// </summary>
public sealed partial class PlayerWindow : Window
{
    /// <summary>Creates.</summary>
    public PlayerWindow()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }
}
