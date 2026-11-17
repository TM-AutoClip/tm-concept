using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
/// password; an older database goes through the schema steps it lacks after a copy of it is kept; one this version cannot open is not
/// changed: the unlock window says why, and for one made before upgrades were recorded offers to set it aside and start fresh) · main window. After the main window opens: old work files are cleaned and the daily backup runs. Closing the main window
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
                ShowUnlock(null, null);
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

    private void ShowUnlock(string? error, Storage.Database.DatabaseVersionException? version)
    {
        var model = services.GetRequiredService<UnlockViewModel>();
        model.Error = error ?? string.Empty;
        if (version is not null)
        {
            model.Show(version);
        }

        var window = new UnlockWindow { DataContext = model };
        model.Completed += async (_, _) =>
        {
            await StartMainAsync().ConfigureAwait(true);
            window.Close();
        };
        model.StartFresh += (_, _) =>
        {
            // The first-run window becomes the active one before the unlock window closes (closing the last one would end the app).
            ShowFirstRun(FirstRunStep.Choice);
            window.Close();
        };
        Present(window);
    }

    private void Present(Window window)
    {
        Os.TaskbarIcon.ApplyWhenOpen(window, logger);
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
        catch (Storage.Database.DatabaseVersionException ex)
        {
            // The key opened the file, but this version cannot use its schema: the unlock window says why and what can be done.
            logger.LogCritical(ex, "The database was not opened: {Problem}", ex.Problem);
            ShowUnlock(null, ex);
        }
        catch (Exception ex)
        {
            // The keyring (DPAPI / Secret Service) failed: the key store cannot open the database; the master password window is the way left.
            logger.LogCritical(ex, "The database could not be opened automatically (key protection: {Method})", vault.Method);
            ShowUnlock(text.Format("unlock.automatic_failed", ex.Message), null);
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
        Os.TaskbarIcon.ApplyWhenOpen(window, logger);
        window.Closing += (_, e) => OnClosing(window, shell, e);
        window.Closed += (_, _) => Quit();
        _main = window;
        windows.Active = window;
        _desktop!.MainWindow = window;
        window.Show();

        // Only now (the database is open and upgraded, the main window is up) is the previous version's program file of an update removed:
        // until here a failed start could still go back to it.
        services.GetRequiredService<Updates.SelfReplacer>().CleanUp();
        await shell.StartAsync().ConfigureAwait(true);
        if (session.PendingMessage is { } message)
        {
            activity.Add(ActivityLevel.Info, message);
            notifier.Notify(new AppNotification(NotificationKind.Info, "TM Concept", message));
            session.PendingMessage = null;
        }

        // The version that ran before, read before the version check records the running one: the "What's new" window shows what changed since.
        var previous = services.GetRequiredService<Paths.UiPreferencesFile>().Read().LastVersion;

        // Another app version ran before: what to do with the installed components (update / reinstall / later). Only here, after the
        // first run or the unlock, in the main window; a failure is logged and shown, it never keeps the app from starting.
        try
        {
            await services.GetRequiredService<VersionCheck>().RunAsync(AppIdentity.ShortVersion, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "The components could not be checked after the version change");
        }

        // What changed since the version that ran before (once, from the notes built into the app); never holds the start up.
        _ = ShowWhatsNewAsync(previous);

        // A newer version of the app (at most once a day, when it is not turned off): offered in a window, installed only on confirmation.
        _ = OfferUpdateAsync();

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
            logger.LogError(LogEvents.OnScreen, ex, "Daily backup failed");
            activity.Add(ActivityLevel.Error, text.Format("backup.failed", ex.Message));
            notifier.Notify(new AppNotification(NotificationKind.Failed, "TM Concept", text.Format("backup.failed", ex.Message)));
        }
    }

    /// <summary>Shows the notes of the versions since <paramref name="previous"/> once; a failure is only logged (the start is never held up).</summary>
    private async Task ShowWhatsNewAsync(string previous)
    {
        try
        {
            await services.GetRequiredService<WhatsNewCheck>().RunAsync(previous, AppIdentity.ShortVersion).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "The notes of the new version could not be shown");
        }
    }

    /// <summary>Looks for a newer version in the background and offers it; a failure is only logged (the start is never held up).</summary>
    private async Task OfferUpdateAsync()
    {
        try
        {
            var updater = services.GetRequiredService<Updates.AppUpdater>();
            var offer = await Task.Run(() => updater.CheckAtStartAsync(CancellationToken.None)).ConfigureAwait(true);
            if (offer is not null)
            {
                await services.GetRequiredService<Dialogs.IAppUpdateDialog>().ShowAsync(offer).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            logger.LogWarning(ex, "The check for a newer version failed");
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
}
