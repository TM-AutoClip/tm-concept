using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Diagnostics;
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
/// changed: the unlock window says why, and for one made before upgrades were recorded offers to set it aside and start fresh) · main window.
/// It takes over from the splash window (<see cref="AppBoot"/>, 0.1.38): the automatic unlock, the database upgrade and the session load run
/// off the interface thread, named on the splash (old work files are cleaned with the session, before any page can start a run); the splash
/// closes when the first-run, unlock or main window opens. After the main window opens: the previous version's program file is removed,
/// leftover install folders are swept and the daily backup runs,
/// all in the background (the app is usable meanwhile). Closing the main window ends the app; while a page works (a generation, a video render) it asks first and stops the work (its Node / Chrome / ffmpeg processes
/// are ended with it) before the app closes. An exception that reaches the interface thread is logged and shown instead of ending the app.
/// Every step until the daily backup writes its time to the log (<c>startup.&lt;step&gt; &lt;ms&gt;</c>, <see cref="IStepTimer"/>);
/// <c>startup.at.&lt;point&gt;</c> lines are the time since <c>Main</c> began.
/// </summary>
public sealed class AppStartup(IServiceProvider services, Vault vault, Localizer text, LanguageResources languageResources, UiPreferencesFile preferences,
    AppSession session, WindowHost windows, BackupService backup, IActivityLog activity, INotifier notifier, CrashLog crash, IStepTimer timer,
    ILogger<AppStartup> logger)
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private Window? _main;
    private StartupSplash? _splash;
    private bool _closeConfirmed;

    /// <summary>Runs the flow; <paramref name="splash"/> is the splash window the start shows until the next window opens.</summary>
    public void Run(IClassicDesktopStyleApplicationLifetime desktop, StartupSplash splash)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(splash);
        _desktop = desktop;
        _splash = splash;
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

    /// <summary>
    /// Shows and activates the main window (second start); before it opens, the splash, first-run or unlock window is brought to the front.
    /// </summary>
    public void ShowMainWindow() => Dispatcher.UIThread.Post(() =>
    {
        if (_main is null)
        {
            ((Window?)_splash?.Window ?? windows.Active)?.Activate();
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
        CloseSplash();
        timer.Mark("startup.at." + (window is FirstRunWindow ? "first_run_window" : "unlock_window"));
    }

    /// <summary>The next window is up: the splash closes (never two start windows on top of each other).</summary>
    private void CloseSplash()
    {
        if (_splash is { } splash)
        {
            _splash = null;
            splash.Window.Finish();
        }
    }

    /// <summary>
    /// The keyring opens the database: the vault, the database upgrade and the session load run off the interface thread, named on the splash;
    /// then the main window opens. A failure of the vault step opens the unlock window, any later one is shown on the splash.
    /// </summary>
    private async Task OpenAutomaticAsync()
    {
        var splash = _splash!;
        var outcome = await splash.Sequence.RunAsync(
        [
            new StartupStep(StartupStage.Vault, OpenVaultAsync, VaultFailed),
            new StartupStep(StartupStage.Session, _ => LoadSessionAsync()),
        ], splash.Screen).ConfigureAwait(true);
        if (outcome != StartupOutcome.Completed)
        {
            return;
        }

        try
        {
            await OpenMainAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogCritical(ex, "The main window could not be opened");
            if (_splash is { } open)
            {
                // Before the main window was up: the splash says why and closes the app (with exit code 1).
                open.Sequence.Fail(open.Screen, StartupStage.Session, ex.Message);
            }
            else
            {
                activity.Add(ActivityLevel.Error, text.Format("error.generic", ex.Message));
            }
        }
    }

    private async Task OpenVaultAsync(IProgress<StartupStage> progress)
    {
        using (timer.Step("startup.vault_open"))
        {
            await vault.OpenAsync(() => progress.Report(StartupStage.Database), CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>The automatic unlock failed: the unlock window takes over (on the interface thread).</summary>
    private bool VaultFailed(Exception ex)
    {
        if (ex is Storage.Database.DatabaseVersionException version)
        {
            // The key opened the file, but this version cannot use its schema: the unlock window says why and what can be done.
            logger.LogCritical(ex, "The database was not opened: {Problem}", version.Problem);
            ShowUnlock(null, version);
        }
        else
        {
            // The keyring (DPAPI / Secret Service) failed: the key store cannot open the database; the master password window is the way left.
            logger.LogCritical(ex, "The database could not be opened automatically (key protection: {Method})", vault.Method);
            ShowUnlock(text.Format("unlock.automatic_failed", ex.Message), null);
        }

        return true;
    }

    /// <summary>
    /// The settings, the selected profile and the local servers, then the kept prices (no network) and the old work files; off the interface
    /// thread, before the main window opens.
    /// </summary>
    private async Task LoadSessionAsync()
    {
        using (timer.Step("startup.session_load", countConnections: true))
        {
            await session.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        }

        using (timer.Step("startup.prices_saved"))
        {
            services.GetRequiredService<Providers.Pricing.PriceUpdater>().LoadSaved();
        }

        CleanWork();
    }

    /// <summary>
    /// The work files a closed or killed run left (<see cref="Core.Videos.VideoPipeline.CleanWork"/>): before the main window opens, so no
    /// video, listen or sample a page starts can own one of them yet (0.1.38 moved it off the interface thread; after the window it would
    /// race the first run). A failure is logged, the start goes on.
    /// </summary>
    private void CleanWork()
    {
        try
        {
            using (timer.Step("startup.clean_work"))
            {
                services.GetRequiredService<Core.Videos.VideoPipeline>().CleanWork();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Old work files could not be cleaned");
        }
    }

    /// <summary>After the first run or the master password: the session loads in the background, then the main window opens.</summary>
    private async Task StartMainAsync()
    {
        await Task.Run(LoadSessionAsync).ConfigureAwait(true);
        await OpenMainAsync().ConfigureAwait(true);
    }

    private async Task OpenMainAsync()
    {
        text.Use(session.Settings.Language);
        // Prices: the last good copies were loaded with the session; a check for newer ones at most once a day, in the background (offline =
        // built-in).
        var prices = services.GetRequiredService<Providers.Pricing.PriceUpdater>();
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
        // The repository's model catalog: read at most once a day in the background; what it adds appears at the next start.
        var remoteCatalog = services.GetRequiredService<Catalog.RemoteCatalogUpdater>();
        _ = Task.Run(async () =>
        {
            try
            {
                await remoteCatalog.UpdateAsync(force: false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                logger.LogWarning(ex, "The daily model catalog check failed; the kept copy stays");
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
        ShellViewModel shell;
        using (timer.Step("startup.main_window"))
        {
            shell = services.GetRequiredService<ShellViewModel>();
            var window = new MainWindow { DataContext = shell };
            Os.TaskbarIcon.ApplyWhenOpen(window, logger);
            window.Closing += (_, e) => OnClosing(window, shell, e);
            window.Closed += (_, _) => Quit();
            _main = window;
            windows.Active = window;
            _desktop!.MainWindow = window;
            window.Show();
            CloseSplash();
        }

        timer.Mark("startup.at.main_window");

        // Only now (the database is open and upgraded, the main window is up) is the previous version's program file of an update removed:
        // until here a failed start could still go back to it. In the background (a file delete never holds the window up).
        var replacer = services.GetRequiredService<Updates.SelfReplacer>();
        _ = Task.Run(() =>
        {
            using (timer.Step("startup.self_replacer_cleanup"))
            {
                replacer.CleanUp();
            }
        });

        using (timer.Step("startup.shell_start", countConnections: true))
        {
            await shell.StartAsync().ConfigureAwait(true);
        }

        if (session.PendingMessage is { } message)
        {
            activity.Add(ActivityLevel.Info, message);
            notifier.Notify(new AppNotification(NotificationKind.Info, "TM Concept", message));
            session.PendingMessage = null;
        }

        // A newer version of the app (looked for at every start unless turned off, a conditional request) is offered BEFORE the components
        // question (0.1.55: 0.1.54 asked first, so the developer installed the components of the old version before he saw the new one). The
        // start waits for the look a few seconds at most; an answer that comes later is offered after the question. Installed only on the
        // user's yes; then the app restarts as the new version, which asks the components question itself.
        var updateCheck = StartUpdateCheck();
        var updates = services.GetRequiredService<IStartUpdateOffer>();
        StartUpdateOutcome firstOffer;
        using (timer.Step("startup.update_offer"))
        {
            firstOffer = await updates.OfferFirstAsync(updateCheck, Updates.UpdateRules.StartWait).ConfigureAwait(true);
        }

        if (firstOffer == StartUpdateOutcome.Installed)
        {
            logger.LogInformation("A newer version was installed at the start; this one closes without asking more");
            return;
        }

        // 28 Nov 2026: an install or a "Reinstall from scratch" the previous run did not finish (closed, crashed) goes on first: the
        // half-finished component's files are deleted for good and what is missing is downloaded again; the finished ones stay.
        try
        {
            using (timer.Step("startup.install_resume"))
            {
                await services.GetRequiredService<Components.InstallResume>().RunAsync(CancellationToken.None).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "The unfinished component installs could not be resumed");
        }

        // Another app version ran before: what to do with the installed components (update / reinstall / later). Only here, after the
        // first run or the unlock, in the main window; a failure is logged and shown, it never keeps the app from starting.
        try
        {
            using (timer.Step("startup.version_check"))
            {
                await services.GetRequiredService<VersionCheck>().RunAsync(AppIdentity.ShortVersion, CancellationToken.None).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "The components could not be checked after the version change");
        }

        // 0.1.37: what an earlier version's "Reinstall from scratch" left next to the components folder, the staging folders of installs and
        // the engines' leftover tmp folders, deleted without a question (28 Nov 2026), in the background (gigabytes take a while; the window
        // never waits). After the version check, so the reinstall it may start has ended.
        var sweeper = services.GetRequiredService<DiskSpace.ILeftoverSweeper>();
        var swept = Task.Run(async () =>
        {
            try
            {
                using (timer.Step("startup.leftover_sweep"))
                {
                    await sweeper.SweepAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                logger.LogWarning(ex, "The start-up sweep of leftover folders failed; it is tried again at the next start");
            }
        });

        // 0.1.54: then the unused model files (old versions, unfinished downloads, files no model uses): listed with their sizes and deleted
        // for good only on the user's yes; at most once a day, from 500 MB on.
        _ = OfferOrphanCleanupAsync(swept);

        // What changed since the version whose notes a start recorded last (once, from the notes built into the app); never holds the start up.
        _ = ShowWhatsNewAsync();

        // The look answered only after the start's wait: what it found is offered now.
        if (firstOffer == StartUpdateOutcome.Pending)
        {
            _ = updates.OfferAsync(updateCheck);
        }

        try
        {
            string? made;
            using (timer.Step("startup.daily_backup", countConnections: true))
            {
                // Off the interface thread: the app is usable while the backup is made.
                made = await Task.Run(() => backup.RunDailyAsync(CancellationToken.None)).ConfigureAwait(true);
            }

            if (made is not null)
            {
                activity.Add(ActivityLevel.Info, text["backup.daily_done"]);
            }
            else if (!await Task.Run(() => backup.HasPasswordAsync(CancellationToken.None)).ConfigureAwait(true))
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

        // The end of the start, and every database connection opened since Main (background checks included).
        timer.Mark("startup.at.done");
        var connections = timer.Connections;
        timer.Record("startup.database_total", connections.Opening, connections);
    }

    /// <summary>Shows the notes of the new versions once; a failure is only logged (the start is never held up).</summary>
    private async Task ShowWhatsNewAsync()
    {
        try
        {
            await services.GetRequiredService<WhatsNewCheck>().RunAsync(AppIdentity.ShortVersion).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Fire and forget: every failure is logged here. The version is already remembered, so the start does not try again; Settings →
            // General → "What's new" still shows the notes.
            logger.LogError(ex, "The notes of the new version could not be shown (Settings → General → What's new shows them)");
        }
    }

    /// <summary>After the start-up sweep: asks about the unused model files (a failure is only logged; the start is never held up).</summary>
    private async Task OfferOrphanCleanupAsync(Task swept)
    {
        try
        {
            await swept.ConfigureAwait(true);
            await services.GetRequiredService<DiskSpace.IOrphanFileCheck>().RunAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogWarning(ex, "The look for unused model files failed; it is tried again at the next start");
        }
    }

    /// <summary>Starts the start's look for a newer version off the window's thread (its failure is seen by <see cref="IStartUpdateOffer"/>).</summary>
    private Task<Updates.UpdateOffer?> StartUpdateCheck()
    {
        var updater = services.GetRequiredService<Updates.AppUpdater>();
        return Task.Run(() => updater.CheckAtStartAsync(CancellationToken.None));
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
