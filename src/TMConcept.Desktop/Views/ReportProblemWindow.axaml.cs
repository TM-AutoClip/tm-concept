using Avalonia.Controls;

namespace TMConcept.Desktop.Views;

/// <summary>The "Report a problem" window.</summary>
public sealed partial class ReportProblemWindow : Window
{
    /// <summary>Creates.</summary>
    public ReportProblemWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
    }
}
