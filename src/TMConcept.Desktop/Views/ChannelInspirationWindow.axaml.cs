using Avalonia.Controls;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Views;

/// <summary>
/// The "Kanaldan esinlen" window (0.1.53): closing it in any way (the title bar's X too) cancels a running look; it closes by itself when the
/// profile was made.
/// </summary>
public sealed partial class ChannelInspirationWindow : Window
{
    private ChannelInspirationViewModel? _model;

    /// <summary>Creates.</summary>
    public ChannelInspirationWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null)
            {
                _model.CloseRequested -= OnCloseRequested;
            }

            _model = DataContext as ChannelInspirationViewModel;
            if (_model is not null)
            {
                _model.CloseRequested += OnCloseRequested;
            }
        };
        Closing += (_, _) => _model?.Cancel();
        CloseButton.Click += (_, _) => Close();
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
