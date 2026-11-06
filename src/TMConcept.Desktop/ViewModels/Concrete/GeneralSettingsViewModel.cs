using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Components;
using TMConcept.Core.Engines;
using TMConcept.Core.Notifications;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// General settings: interface language (changes at once), drawing (after a restart), the bottom-right notifications, and the installed
/// components (read-only list with sizes of what is on disk, an incomplete one marked; Remove after a confirmation; a ready engine can
/// render its sample; on Linux the apt command for Chrome's missing libraries). Components are installed when a process that needs them starts (after one confirmation).
/// </summary>
public sealed partial class GeneralSettingsViewModel(AppSession session, Localizer text, UiPreferencesFile preferences, INotifier notifier,
    ComponentPlanner components, IToolPlatform platform, Engines.Engines.SampleRender sample, IConfirmDialog confirm, ShellLauncher shell,
    IActivityLog activity, ILogger<GeneralSettingsViewModel> logger)
    : SettingsSectionBase
{
    /// <inheritdoc />
    public override string TitleKey => "settings.general";

    /// <inheritdoc />
    public override int Order => 10;

    /// <summary>Notifications on.</summary>
    [ObservableProperty]
    public partial bool NotificationsEnabled { get; set; }

    /// <summary>How long a notification stays (seconds).</summary>
    [ObservableProperty]
    public partial decimal? NotificationSeconds { get; set; }

    /// <summary>Draw with the graphics card (stored at once, used from the next start).</summary>
    [ObservableProperty]
    public partial bool UseGraphicsCard { get; set; }

    /// <summary>Installed components.</summary>
    public ObservableCollection<ComponentRowViewModel> Installed { get; } = [];

    /// <summary>Whether nothing is installed yet.</summary>
    [ObservableProperty]
    public partial bool NothingInstalled { get; set; }

    /// <summary>apt command for Chrome's missing libraries (Linux; empty when none are missing).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingLibraries))]
    public partial string MissingLibraries { get; set; } = string.Empty;

    /// <summary>Whether libraries are missing.</summary>
    public bool HasMissingLibraries => MissingLibraries.Length > 0;

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        _loading = true;
        UseGraphicsCard = preferences.Read().UseGraphicsCard;
        _loading = false;
        NotificationsEnabled = session.Settings.NotificationsEnabled;
        NotificationSeconds = session.Settings.NotificationSeconds;
        await RefreshInstalledAsync().ConfigureAwait(true);
    }

    /// <summary>Removes a component after a confirmation (it is downloaded again when a setting needs it).</summary>
    [RelayCommand]
    public async Task RemoveAsync(ComponentRowViewModel? row)
    {
        if (row is null || row.Busy)
        {
            return;
        }

        // What needs it stops working until it is downloaded again: said in the question (they can be removed on their own rows).
        var dependents = Installed.Where(r => r.Component.DependsOn.Contains(row.Component.Code)).Select(r => r.Name).ToList();
        var message = text.Format("components.remove.message", row.Name, row.Size)
            + (dependents.Count > 0 ? "\n\n" + text.Format("components.remove.dependents", string.Join(", ", dependents)) : string.Empty);
        var answer = await confirm.AskAsync(text["components.remove.title"], message, text["components.remove.confirm"], danger: true).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        row.Busy = true;
        try
        {
            await components.RemoveAsync(row.Component, CancellationToken.None).ConfigureAwait(true);
            activity.Add(ActivityLevel.Info, text.Format("components.removed", row.Name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "{Component} could not be removed", row.Component.Code);
            activity.Add(ActivityLevel.Error, text.Format("components.remove_failed", row.Name, ex.Message));
        }
        finally
        {
            row.Busy = false;
        }

        await RefreshInstalledAsync().ConfigureAwait(true);
    }

    /// <summary>Renders an engine's sample (install check; no model, no network) and opens its folder.</summary>
    [RelayCommand]
    public async Task SampleAsync(ComponentRowViewModel? row)
    {
        if (row?.Component is not Engines.Components.EngineComponent { Engine: var engine } || row.Busy)
        {
            return;
        }

        row.Busy = true;
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            logger.LogInformation("Sample render of {Engine} started", engine.Name);
            var video = await sample.RenderAsync(engine, null, CancellationToken.None).ConfigureAwait(true);
            logger.LogInformation("Sample render of {Engine} finished after {Seconds:0.0} s", engine.Name, watch.Elapsed.TotalSeconds);
            activity.Add(ActivityLevel.Info, text.Format("engines.sample_done", engine.Name));
            shell.OpenPath(Path.GetDirectoryName(video)!);
        }
        catch (EngineException ex)
        {
            logger.LogError(ex, "Sample render of {Engine} failed ({Code})", engine.Name, ex.Code);
            activity.Add(ActivityLevel.Error, text.Format("engines.sample_failed", engine.Name, ex.Message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ToolInstallException or InvalidOperationException)
        {
            logger.LogError(ex, "Sample render of {Engine} failed", engine.Name);
            activity.Add(ActivityLevel.Error, text.Format("engines.sample_failed", engine.Name, ex.Message));
        }
        finally
        {
            row.Busy = false;
        }
    }

    private async Task RefreshInstalledAsync()
    {
        // A Node.js on the PATH makes the engines ready only after it was looked at (once per run).
        await components.RefreshAllAsync(CancellationToken.None).ConfigureAwait(true);

        // Listed by what is on disk, not only what is ready: weights whose program was removed, packages without Node.js or ffmpeg and a
        // half-finished download still take space and must be removable ("incomplete"). The system's copy of a program shows when ready.
        var rows = await Task.Run(() => components.All.Select(c =>
        {
            var ready = c.Ready;
            if (c.Folders().Count == 0)
            {
                return ready ? new ComponentRowViewModel(c, text["components.system"], false, false) : null;
            }

            var size = components.Size(c);
            return ready || size > 0
                ? new ComponentRowViewModel(c, ComponentInstallFlow.Megabytes(size) + " MB", ready && c is Engines.Components.EngineComponent, !ready)
                : null;
        }).OfType<ComponentRowViewModel>().ToList()).ConfigureAwait(true);
        Installed.Clear();
        foreach (var row in rows)
        {
            Installed.Add(row);
        }

        NothingInstalled = Installed.Count == 0;
        // Chrome's libraries matter only once an engine is there (Linux; Windows has none to check).
        IReadOnlyList<string> missing = Installed.Any(r => r.Component is Engines.Components.EngineComponent && !r.Incomplete)
            ? await platform.MissingLibrariesAsync(CancellationToken.None).ConfigureAwait(true)
            : [];
        MissingLibraries = missing.Count == 0 ? string.Empty : "sudo apt install " + string.Join(' ', missing);
    }

    private bool _loading;

    partial void OnUseGraphicsCardChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        preferences.Change(p => p.UseGraphicsCard = value);
        logger.LogInformation("Setting changed: draw with the {Renderer} after the next start", value ? "graphics card" : "processor");
        Status = text["settings.drawing.restart"];
    }

    [RelayCommand]
    private Task TurkishAsync() => UseLanguageAsync("tr");

    [RelayCommand]
    private Task EnglishAsync() => UseLanguageAsync("en");

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = session.Settings;
        settings.NotificationsEnabled = NotificationsEnabled;
        settings.NotificationSeconds = (int)Math.Clamp(NotificationSeconds ?? 60, 3, 3600);
        await session.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);
        logger.LogInformation("Setting changed: notifications {State}, shown for {Seconds} s", settings.NotificationsEnabled ? "on" : "off",
            settings.NotificationSeconds);
        Status = text["common.saved"];
    }

    [RelayCommand]
    private void TestNotification() =>
        notifier.Notify(new AppNotification(NotificationKind.Info, "TM Concept", text["settings.notification_test"]));

    private async Task UseLanguageAsync(string code)
    {
        text.Use(code);
        preferences.Change(p => p.Language = code);
        var settings = session.Settings;
        settings.Language = code;
        await session.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);
        logger.LogInformation("Setting changed: interface language {Language}", code);
        Status = string.Empty;
    }
}
