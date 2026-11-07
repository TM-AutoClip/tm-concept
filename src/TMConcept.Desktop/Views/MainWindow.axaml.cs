using Avalonia.Controls;

namespace TMConcept.Desktop.Views;

/// <summary>The main window: the sidebar and the current page (the activity panel at the bottom is gone since 0.1.7; lines go to the log file).</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Creates.</summary>
    public MainWindow() => InitializeComponent();
}
