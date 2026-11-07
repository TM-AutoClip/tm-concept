using Avalonia.Controls;
using TMConcept.Desktop.Dialogs;

namespace TMConcept.Desktop.Views;

/// <summary>The modal install window: it cannot be closed while the install runs (Cancel stops it); it closes when its model asks.</summary>
public sealed partial class InstallWindow : Window
{
    private InstallViewModel? _model;

    /// <summary>Creates.</summary>
    public InstallWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null)
            {
                _model.CloseRequested -= OnCloseRequested;
            }

            _model = DataContext as InstallViewModel;
            if (_model is not null)
            {
                _model.CloseRequested += OnCloseRequested;
            }
        };
        Closing += (_, e) => e.Cancel = _model is { Running: true };
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
