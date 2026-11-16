using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Views;

/// <summary>The detailed log page (<see cref="Dialogs.LogViewer"/>).</summary>
public sealed partial class LogWindow : Window
{
    /// <summary>Creates.</summary>
    public LogWindow() => InitializeComponent();

    /// <summary>Copies the shown entries (as they stand in the files) to the clipboard.</summary>
    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LogViewerViewModel model && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(model.CopyText()).ConfigureAwait(true);
        }
    }
}
