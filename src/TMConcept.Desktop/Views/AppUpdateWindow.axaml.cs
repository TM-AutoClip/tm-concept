using Avalonia.Controls;
using TMConcept.Desktop.Dialogs;

namespace TMConcept.Desktop.Views;

/// <summary>
/// The window that offers a newer version of the app (<see cref="Dialogs.AppUpdateDialog"/>). Closing it in any way (the title bar's X too)
/// cancels a download that is running: the app never replaces itself and restarts with no window on screen.
/// </summary>
public sealed partial class AppUpdateWindow : Window
{
    /// <summary>Creates.</summary>
    public AppUpdateWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (DataContext as AppUpdateViewModel)?.Cancel();
    }
}
