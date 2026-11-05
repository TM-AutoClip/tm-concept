using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Views;

/// <summary>The main window. The activity log follows its newest line (an error added at the bottom must be seen).</summary>
public sealed partial class MainWindow : Window
{
    private ShellViewModel? _shell;

    /// <summary>Creates.</summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as ShellViewModel);
    }

    private void Attach(ShellViewModel? shell)
    {
        if (_shell is not null)
        {
            _shell.Log.CollectionChanged -= OnLogChanged;
        }

        _shell = shell;
        if (shell is not null)
        {
            shell.Log.CollectionChanged += OnLogChanged;
        }
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && this.FindControl<ScrollViewer>("LogScroll") is { } scroll)
        {
            Dispatcher.UIThread.Post(scroll.ScrollToEnd, DispatcherPriority.Background);
        }
    }
}
