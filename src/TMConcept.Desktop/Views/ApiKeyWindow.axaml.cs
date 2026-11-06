using Avalonia.Controls;

namespace TMConcept.Desktop.Views;

/// <summary>The key gate's window: add a working API key before a process starts.</summary>
public sealed partial class ApiKeyWindow : Window
{
    /// <summary>Creates.</summary>
    public ApiKeyWindow()
    {
        InitializeComponent();
        CancelButton.Click += (_, _) => Close();
    }
}
