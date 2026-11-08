using Avalonia.Controls;

namespace TMConcept.Desktop.Views;

/// <summary>The main window: the sidebar, the current page and the one-line activity strip at the bottom (<see cref="ViewModels.ShellViewModel"/>).</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Creates.</summary>
    public MainWindow() => InitializeComponent();
}
