using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;

namespace TMConcept.Desktop.Dialogs;

/// <summary>
/// <see cref="ILogViewer"/> as one non-modal window next to the main window: a new page each time it opens (it reads the files again), today's
/// files followed about every second while it is open, nothing read while it is closed.
/// </summary>
public sealed class LogViewer(WindowHost host, Func<LogViewerViewModel> models, ILogger<LogViewer> logger) : ILogViewer
{
    private LogWindow? _window;

    /// <inheritdoc />
    public void Show()
    {
        if (_window is { } open)
        {
            open.Activate();
            return;
        }

        var model = models();
        model.Load();
        var window = new LogWindow { DataContext = model };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            try
            {
                await model.TailAsync().ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "The log files could not be followed");
            }
        };
        window.Closed += (_, _) =>
        {
            timer.Stop();
            _window = null;
        };
        _window = window;
        logger.LogInformation("Log page opened");
        timer.Start();
        if (host.Active is { } owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }
}
