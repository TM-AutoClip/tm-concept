using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Models;
using TMConcept.Core.Notifications;
using TMConcept.Desktop.Composition;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Keys;

namespace TMConcept.Desktop.Startup;

/// <summary>
/// Startup flow: first run (language → new start or restore → protection and backup password) · unlock (automatic keyring, or master
/// password; a database of another version is not opened, the unlock window offers to set it aside and start fresh) · main window. After the main window opens: old work files are cleaned and the daily backup runs. Closing the main window
/// ends the app; while a page works (a generation, a video render) it asks first and stops the work (its Node / Chrome / ffmpeg processes
/// are ended with it) before the app closes. An exception that reaches the interface thread is logged and shown instead of ending the app.
/// </summary>
public sealed class AppStartup(IServiceProvider services, Vault vault, Localizer text, LanguageResources languageResources, UiPreferencesFile preferences,
    AppSession session, WindowHost windows, BackupService backup, IActivityLog activity, INotifier notifier, CrashLog crash, ILogger<AppStartup> logger)
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private Window? _main;
    private bool _closeConfirmed;

    /// <summary>Runs the flow.</summary>
    public void Run(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        _desktop = desktop;
        // Every window (dialogs and the full-screen player too) gets the app icon by default, so the taskbar never shows the framework's icon.
        // Metadata can be overridden once per process (tests start the app more than once).
        if (Interlocked.Exchange(ref s_iconSet, 1) == 0)
        {
            Window.IconProperty.OverrideDefaultValue<Window>(AppIcon());
        }

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            // A command that throws (a storage error, a full disk) must not take the whole app down: logged with its cause and shown.
            crash.Interface(e.Exception);
            activity.Add(ActivityLevel.Error, text.Format("error.generic", e.Exception.Message));
            notifier.Notify(new AppNotification(NotificationKind.Failed, "TM Concept", text.Format("error.generic", e.Exception.Message)));
            e.Handled = true;
        };
        Converters.FileBitmapConverter.Logger = services.GetRequiredService<ILogger<Converters.FileBitmapConverter>>();
        Application.Current!.DataTemplates.Add(services.GetRequiredService<ViewLocator>());
        var language = preferences.Read().Language;
        text.Use(language.Length > 0 ? language : Localizer.SystemLanguage());
        languageResources.Attach(Application.Current.Resources.MergedDictionaries);
        switch (vault.State)
        {
            case VaultState.NotCreated:
                ShowFirstRun(language.Length == 0 ? FirstRunStep.Language : FirstRunStep.Choice);
                break;
            case VaultState.Locked when vault.Method is { } method && method != KeyMethods.Password:
                _ = OpenAutomaticAsync();
                break;
            default:
                ShowUnlock(null);
                break;
        }
    }

    /// <summary>Shows and activates the main window (second start).</summary>
    public void ShowMainWindow() => Dispatcher.UIThread.Post(() =>
    {
        if (_main is null)
        {
            return;
        }

        _main.Show();
        if (_main.WindowState == WindowState.Minimized)
        {
            _main.WindowState = WindowState.Normal;
        }

        _main.Activate();
    });

    /// <summary>Ends the app.</summary>
    public void Quit()
    {
        logger.LogInformation("Quit requested");
        _desktop?.Shutdown();
    }

    private void ShowFirstRun(FirstRunStep step)
    {
        var model = services.GetRequiredService<FirstRunViewModel>();
        model.Step = step;
        var window = new FirstRunWindow { DataContext = model };
        model.Completed += async (_, _) =>
        {
            await StartMainAsync().ConfigureAwait(true);
            window.Close();
        };
        Present(window);
    }

    private void ShowUnlock(string? error)
    {
        var model = services.GetRequiredService<UnlockViewModel>();
        model.Error = error ?? string.Empty;

        var window = new UnlockWindow { DataContext = model };
        model.Completed += async (_, _) =>
        {
            await StartMainAsync().ConfigureAwait(true);
            window.Close();
        };
        Present(window);
    }

    private void Present(Window window)
    {
        window.Closed += (_, _) =>
        {
            if (_main is null && windows.Active == window)
            {
                // The first-run or unlock window was closed without finishing: nothing to run.
                Quit();
            }
        };
        windows.Active = window;
        _desktop!.MainWindow = window;
        window.Show();
    }

    private async Task OpenAutomaticAsync()
    {
        try
        {
            await vault.OpenAsync(CancellationToken.None).ConfigureAwait(true);
            await StartMainAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // The keyring (DPAPI / Secret Service) failed: the key store cannot open the database; the master password window is the way left.
            logger.LogCritical(ex, "The database could not be opened automatically (key protection: {Method})", vault.Method);
            ShowUnlock(text.Format("unlock.automatic_failed", ex.Message));
        }
    }

    private async Task StartMainAsync()
    {
        await session.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        text.Use(session.Settings.Language);
        // Prices: the last good copies now (no network); a check for newer ones at most once a day, in the background (offline = built-in).
        var prices = services.GetRequiredService<Providers.Pricing.PriceUpdater>();
        prices.LoadSaved();
        _ = Task.Run(async () =>
        {
            try
            {
                await prices.UpdateAsync(force: false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogWarning(ex, "The daily price check failed; the last good prices stay");
            }
        });
        // What is on the computer (a Node.js on the PATH counts as installed only once it was looked at): in the background, before the
        // first page reads a status (the pages wait for the same look themselves).
        var components = services.GetRequiredService<Core.Components.ComponentPlanner>();
        _ = Task.Run(async () =>
        {
            try
            {
                await components.RefreshAllAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The installed components could not be looked at");
            }
        });
        var shell = services.GetRequiredService<ShellViewModel>();
        var window = new MainWindow { DataContext = shell };
        window.Closing += (_, e) => OnClosing(window, shell, e);
        window.Closed += (_, _) => Quit();
        _main = window;
        windows.Active = window;
        _desktop!.MainWindow = window;
        window.Show();
        await shell.StartAsync().ConfigureAwait(true);
        if (session.PendingMessage is { } message)
        {
            activity.Add(ActivityLevel.Info, message);
            notifier.Notify(new AppNotification(NotificationKind.Info, "TM Concept", message));
            session.PendingMessage = null;
        }

        try
        {
            services.GetRequiredService<Core.Videos.VideoPipeline>().CleanWork();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Old work files could not be cleaned");
        }

        try
        {
            if (await backup.RunDailyAsync(CancellationToken.None).ConfigureAwait(true) is not null)
            {
                activity.Add(ActivityLevel.Info, text["backup.daily_done"]);
            }
            else if (!await backup.HasPasswordAsync(CancellationToken.None).ConfigureAwait(true))
            {
                activity.Add(ActivityLevel.Warning, text["backup.no_password"]);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Daily backup failed");
            activity.Add(ActivityLevel.Error, text.Format("backup.failed", ex.Message));
            notifier.Notify(new AppNotification(NotificationKind.Failed, "TM Concept", text.Format("backup.failed", ex.Message)));
        }
    }

    /// <summary>While work runs: asks, stops the work (waits at most 20 s for it to end its processes), then closes.</summary>
    private void OnClosing(Window window, ShellViewModel shell, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || !shell.AnyWorking)
        {
            return;
        }

        e.Cancel = true;
        _ = CloseAfterWorkAsync(window, shell);
    }

    private async Task CloseAfterWorkAsync(Window window, ShellViewModel shell)
    {
        try
        {
            var answer = await services.GetRequiredService<Dialogs.IConfirmDialog>().AskAsync(text["close.busy.title"], text["close.busy.message"],
                text["close.busy.confirm"], danger: true).ConfigureAwait(true);
            if (!answer.Confirmed)
            {
                return;
            }

            logger.LogInformation("Closing while work runs: stopping it first");
            shell.CancelWork();
            for (var i = 0; i < 200 && shell.AnyWorking; i++)
            {
                await Task.Delay(100).ConfigureAwait(true);
            }

            if (shell.AnyWorking)
            {
                logger.LogWarning("The work did not stop within 20 s; closing anyway");
            }

            // Whatever still runs (a render that ignored the cancel) ends now, not with the process.
            services.GetRequiredService<Engines.Processes.ChildProcessGuardBase>().KillAll();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Closing with running work failed; closing anyway");
        }

        _closeConfirmed = true;
        window.Close();
    }

    private static int s_iconSet;

    /// <summary>The multi-size .ico (16–256): Windows picks the small frame for the taskbar and title bar instead of shrinking one big picture.</summary>
    private static WindowIcon AppIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://TMConcept/Assets/app.ico"));
        return new WindowIcon(stream);
    }
}
