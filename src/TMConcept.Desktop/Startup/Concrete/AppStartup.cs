using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Desktop.Composition;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Keys;

namespace TMConcept.Desktop.Startup;

/// <summary>
/// Startup flow: first run (language → new start or restore → protection and backup password) · unlock (automatic keyring, or master
/// password) · main window. After the main window opens: the daily backup. Closing the main window ends the app.
/// </summary>
public sealed class AppStartup(IServiceProvider services, Vault vault, Localizer text, LanguageResources languageResources, UiPreferencesFile preferences,
    AppSession session, WindowHost windows, BackupService backup, IActivityLog activity, ILogger<AppStartup> logger)
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private Window? _main;

    /// <summary>Runs the flow.</summary>
    public void Run(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        _desktop = desktop;
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
        window.Icon = AppIcon();
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
            logger.LogError(ex, "The database could not be opened automatically");
            ShowUnlock(text.Format("unlock.automatic_failed", ex.Message));
        }
    }

    private async Task StartMainAsync()
    {
        await session.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        text.Use(session.Settings.Language);
        var shell = services.GetRequiredService<ShellViewModel>();
        var window = new MainWindow { DataContext = shell, Icon = AppIcon() };
        window.Closed += (_, _) => Quit();
        _main = window;
        windows.Active = window;
        _desktop!.MainWindow = window;
        window.Show();
        await shell.StartAsync().ConfigureAwait(true);
        if (session.PendingMessage is { } message)
        {
            activity.Add(ActivityLevel.Info, message);
            session.PendingMessage = null;
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
        }
    }

    private static WindowIcon AppIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://TMConcept/Assets/app-256.png"));
        return new WindowIcon(stream);
    }
}
