using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Paths;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// Starts the new version with <see cref="UpdateRules.AfterUpdateArgument"/> and this process's id (it waits until this one has ended and
/// released the data folder) and the same data folder option, then closes the app the normal way (its children end with it). The install
/// calls it on a pool thread (after its awaits): the close is posted to the UI thread, because Avalonia's windows may only be touched there
/// (0.1.23 closed from the pool thread, threw "a different thread owns it" and said the new version could not start although it had).
/// </summary>
public sealed class AppRestarter(ILogger<AppRestarter> logger) : IAppRestarter
{
    /// <inheritdoc />
    public void Restart(string program)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        var start = new ProcessStartInfo(program) { UseShellExecute = false };
        start.ArgumentList.Add(UpdateRules.AfterUpdateArgument);
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        if (StartOptions.Parse(Environment.GetCommandLineArgs().Skip(1).ToList()).DataDirectory is { } data)
        {
            start.ArgumentList.Add("--data-dir");
            start.ArgumentList.Add(data);
        }

        using (Process.Start(start))
        {
            logger.LogInformation("The new version was started; this one closes");
        }

        OnUiThread(static () => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown());
    }

    /// <summary>Runs <paramref name="action"/> at once on the UI thread, or posts it there from any other thread.</summary>
    internal static void OnUiThread(Action action) => OnUiThread(action, Dispatcher.UIThread.CheckAccess, a => Dispatcher.UIThread.Post(a));

    /// <summary>
    /// <see cref="OnUiThread(Action)"/> with the dispatcher's two calls given (the headless test dispatcher lets every thread in, so a test
    /// gives its own).
    /// </summary>
    internal static void OnUiThread(Action action, Func<bool> onUiThread, Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(onUiThread);
        ArgumentNullException.ThrowIfNull(post);
        if (onUiThread())
        {
            action();
        }
        else
        {
            post(action);
        }
    }
}
