using Avalonia.Controls;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Views;

/// <summary>
/// The small logo window a start shows at once (0.1.38), before any service is built; it closes when the first-run, unlock or main window
/// opens (<see cref="Finish"/>). Closed any other way (Alt+F4) it cancels the start like its Cancel button.
/// </summary>
public sealed partial class SplashWindow : Window
{
    private bool _finished;

    /// <summary>Creates.</summary>
    public SplashWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (!_finished && DataContext is SplashViewModel model)
            {
                e.Cancel = true;
                model.CancelCommand.Execute(null);
            }
        };
    }

    /// <summary>The start went on to its next window: closes without canceling.</summary>
    public void Finish()
    {
        _finished = true;
        Close();
    }
}
