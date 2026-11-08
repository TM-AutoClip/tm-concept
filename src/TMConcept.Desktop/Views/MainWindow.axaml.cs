using Avalonia.Controls;

namespace TMConcept.Desktop.Views;

/// <summary>The main window: the sidebar, the current page and the activity log panel at the bottom (<see cref="ViewModels.ShellViewModel"/>).</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Creates.</summary>
    public MainWindow() => InitializeComponent();
}
