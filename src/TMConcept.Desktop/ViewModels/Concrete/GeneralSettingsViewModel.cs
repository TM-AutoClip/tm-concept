using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Components;
using TMConcept.Core.Engines;
using TMConcept.Core.Hardware;
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
/// General settings: interface language (changes at once), drawing (after a restart), the bottom-right notifications, the answer limit of
/// "Suggest topics" (saved with the notifications' Save button), and the installed
/// components (read-only list with sizes of what is on disk, an incomplete one marked; Remove after a confirmation; a ready engine can
/// render its sample; on Linux the apt command for Chrome's missing libraries). Components are installed when a process that needs them starts (after one confirmation).
/// Image acceleration (per computer): which stable-diffusion.cpp build runs the local image models, with what Automatic picks here (the
/// card found and its memory); a change installs that build at once when a local image model is already on disk (one confirmation).
/// </summary>
public sealed partial class GeneralSettingsViewModel(AppSession session, Localizer text, UiPreferencesFile preferences, INotifier notifier,
    ComponentPlanner components, IToolPlatform platform, Engines.Engines.SampleRender sample, IConfirmDialog confirm, ShellLauncher shell,
    IActivityLog activity, PreferencesImageAcceleration acceleration, Engines.Images.ImageBackendSelector backends, ComponentInstallFlow installs,
    ILogger<GeneralSettingsViewModel> logger, ComponentReinstaller reinstaller, Updates.AppUpdater updater, IAppUpdateDialog updateDialog)
    : SettingsSectionBase
{
    /// <summary>Look for a newer version of the app at start (stored at once, per computer).</summary>
    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    /// <summary>What "Check now" found ("0.1.18 is available", "You have the newest version").</summary>
    [ObservableProperty]
    public partial string UpdateStatus { get; set; } = string.Empty;

    /// <summary>"Check now" is looking.</summary>
    [ObservableProperty]
    public partial bool CheckingUpdates { get; set; }

    /// <summary>The running version (shown next to the update settings).</summary>
    public string RunningVersion => text.Format("appupdate.running", updater.Current);

    /// <summary>Asks GitHub now (also within the day and when the check at start is off) and offers a newer version in its window.</summary>
    [RelayCommand]
    public async Task CheckUpdatesAsync()
    {
        if (CheckingUpdates)
        {
            return;
        }

        CheckingUpdates = true;
        UpdateStatus = text["appupdate.checking"];
        try
        {
            var offer = await Task.Run(() => updater.CheckAsync(force: true, CancellationToken.None)).ConfigureAwait(true);
            if (offer is null)
            {
                UpdateStatus = text.Format("appupdate.current", updater.Current);
                return;
            }

            UpdateStatus = text.Format("appupdate.found", offer.Version);
            await updateDialog.ShowAsync(offer).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            logger.LogWarning(ex, "Check now for a newer version failed");
            UpdateStatus = text.Format("appupdate.check_failed", ex.Message);
        }
        finally
        {
            CheckingUpdates = false;
        }
    }

    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        try
        {
            preferences.Change(p => p.CheckForUpdates = value);
            logger.LogInformation("Setting changed: look for a newer version at start {State}", value ? "on" : "off");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The update check setting could not be kept");
        }
    }

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

    /// <summary>Answer limit (max_tokens) of "Suggest topics" (saved with the Save button, default 16 000).</summary>
    [ObservableProperty]
    public partial decimal? TopicMaxOutputTokens { get; set; }

    /// <summary>The field's range: <see cref="Core.Concepts.TopicLimits.OutputMin"/>-<see cref="Core.Concepts.TopicLimits.OutputLimit"/> (the view holds the same numbers).</summary>
    public static (int Min, int Max) TopicOutputRange => (Core.Concepts.TopicLimits.OutputMin, Core.Concepts.TopicLimits.OutputLimit);

    /// <summary>Draw with the graphics card (stored at once, used from the next start).</summary>
    [ObservableProperty]
    public partial bool UseGraphicsCard { get; set; }

    /// <summary>The Image acceleration choices (Automatic, NVIDIA CUDA, Vulkan, Processor) in the current language.</summary>
    public ObservableCollection<ChoiceOption> AccelerationOptions { get; } = [];

    /// <summary>The chosen Image acceleration.</summary>
    [ObservableProperty]
    public partial ChoiceOption? Acceleration { get; set; }

    /// <summary>What Automatic chooses on this computer: "AMD Radeon RX 7800 XT · 16 GB → Vulkan (AMD, Intel, NVIDIA)".</summary>
    [ObservableProperty]
    public partial string AccelerationNote { get; set; } = string.Empty;

    /// <summary>The work started by the last Image acceleration change (its install); finished when nothing runs.</summary>
    public Task AccelerationApplied { get; private set; } = Task.CompletedTask;

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
        CheckForUpdates = preferences.Read().CheckForUpdates;
        AccelerationOptions.Clear();
        foreach (var value in Enum.GetValues<ImageAcceleration>())
        {
            AccelerationOptions.Add(new ChoiceOption(value.ToString(), text[AccelerationKey(value)]));
        }

        Acceleration = AccelerationOptions.FirstOrDefault(o => o.Value == acceleration.Value.ToString());
        _loading = false;
        // The first call looks at the graphics cards (native calls; on Linux maybe a short nvidia-smi run): off the UI thread.
        AccelerationNote = Detected(await Task.Run(backends.Automatic).ConfigureAwait(true));
        NotificationsEnabled = session.Settings.NotificationsEnabled;
        NotificationSeconds = session.Settings.NotificationSeconds;
        TopicMaxOutputTokens = Core.Concepts.TopicSuggester.ClampOutput(session.Settings.TopicMaxOutputTokens);
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
            Status = text.Format("components.remove_failed", row.Name, ex.Message);
        }
        finally
        {
            row.Busy = false;
        }

        await RefreshInstalledAsync().ConfigureAwait(true);
    }

    /// <summary>Where the components live (shown under the list's title).</summary>
    public string ToolsLocation => text.Format("components.location", reinstaller.Location());

    /// <summary>A reinstall runs.</summary>
    [ObservableProperty]
    public partial bool Reinstalling { get; set; }

    /// <summary>
    /// "Reinstall components from scratch" (the same as after a new app version): after a confirmation the whole components folder goes to
    /// the recycle bin and what was installed is installed again. Not while work runs: its components are in use.
    /// </summary>
    [RelayCommand]
    public async Task ReinstallComponentsAsync()
    {
        if (Reinstalling)
        {
            return;
        }

        if (session.WorkRunning)
        {
            Status = text["components.reinstall.busy"];
            activity.Add(ActivityLevel.Warning, Status);
            return;
        }

        var answer = await confirm.AskAsync(text["components.reinstall.title"], text.Format("components.reinstall.message", reinstaller.Location()),
            text["components.reinstall"], danger: true).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        Reinstalling = true;
        try
        {
            var outcome = await reinstaller.ReinstallAsync(CancellationToken.None).ConfigureAwait(true);
            Status = outcome.Ready ? text["components.reinstall.done"] : outcome.Reason;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "The components could not be reinstalled");
            Status = text.Format("components.reinstall.failed", ex.Message);
            activity.Add(ActivityLevel.Error, Status);
        }
        finally
        {
            Reinstalling = false;
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
            Status = text.Format("engines.sample_done", engine.Name);
            shell.OpenPath(Path.GetDirectoryName(video)!);
        }
        catch (EngineException ex)
        {
            logger.LogError(ex, "Sample render of {Engine} failed ({Code})", engine.Name, ex.Code);
            activity.Add(ActivityLevel.Error, text.Format("engines.sample_failed", engine.Name, ex.Message));
            Status = text.Format("engines.sample_failed", engine.Name, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ToolInstallException or InvalidOperationException)
        {
            logger.LogError(ex, "Sample render of {Engine} failed", engine.Name);
            activity.Add(ActivityLevel.Error, text.Format("engines.sample_failed", engine.Name, ex.Message));
            Status = text.Format("engines.sample_failed", engine.Name, ex.Message);
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

    /// <summary>The language key of a setting value.</summary>
    internal static string AccelerationKey(ImageAcceleration value) => value switch
    {
        ImageAcceleration.Cuda => "settings.image_acceleration.cuda",
        ImageAcceleration.Vulkan => "settings.image_acceleration.vulkan",
        ImageAcceleration.Cpu => "settings.image_acceleration.cpu",
        _ => "settings.image_acceleration.automatic",
    };

    /// <summary>The language key of a build.</summary>
    internal static string BackendKey(ImageBackend backend) => backend switch
    {
        ImageBackend.Cuda => "settings.image_acceleration.cuda",
        ImageBackend.Vulkan => "settings.image_acceleration.vulkan",
        _ => "settings.image_acceleration.cpu",
    };

    /// <summary>"Automatic choice on this computer: AMD Radeon RX 7800 XT · 16 GB → Vulkan (AMD, Intel, NVIDIA)".</summary>
    internal string Detected(ImageBackendChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        var card = choice.Device is { } device
            ? device.Name + " · " + (device.VideoMemoryBytes is { } bytes
                ? Math.Round(bytes / (1024.0 * 1024 * 1024), 1).ToString("0.#", text.Culture) + " GB"
                : text["settings.image_acceleration.memory_unknown"])
            : text["settings.image_acceleration.no_card"];
        return text.Format("settings.image_acceleration.detected", card, text[BackendKey(choice.Backend)]);
    }

    partial void OnAccelerationChanged(ChoiceOption? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        var chosen = Enum.GetValues<ImageAcceleration>().FirstOrDefault(v => v.ToString() == value.Value);
        if (chosen == acceleration.Value)
        {
            return;
        }

        acceleration.Change(chosen);
        AccelerationApplied = ApplyAccelerationAsync(chosen);
    }

    /// <summary>
    /// After a change: the build it needs is installed at once when a local image model is already on disk (one confirmation, the usual
    /// install window); otherwise it comes with the first local image model.
    /// </summary>
    private async Task ApplyAccelerationAsync(ImageAcceleration chosen)
    {
        var choice = await Task.Run(backends.Choose).ConfigureAwait(true);
        logger.LogInformation("Setting changed: image acceleration {Setting}; the local image models use the {Backend} build", chosen, choice.Backend);
        Status = text.Format("settings.image_acceleration.saved", text[BackendKey(choice.Backend)]);
        var imagesOnDisk = await Task.Run(() => components.All.Any(c => c.Code.StartsWith(ComponentCodes.ImagePrefix, StringComparison.Ordinal) && components.Size(c) > 0))
            .ConfigureAwait(true);
        if (!imagesOnDisk)
        {
            return;
        }

        var outcome = await installs.EnsureAsync([ComponentCodes.ImageRuntime], "install.purpose.image_acceleration", null, CancellationToken.None)
            .ConfigureAwait(true);
        if (outcome.Reason.Length > 0)
        {
            Status = outcome.Reason;
        }

        await RefreshInstalledAsync().ConfigureAwait(true);
    }

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
        settings.TopicMaxOutputTokens = Core.Concepts.TopicSuggester.ClampOutput((int)(TopicMaxOutputTokens ?? Core.Concepts.TopicSuggester.OutputMax));
        TopicMaxOutputTokens = settings.TopicMaxOutputTokens;
        await session.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);
        logger.LogInformation("Setting changed: notifications {State}, shown for {Seconds} s; topic answer limit {Tokens} tokens",
            settings.NotificationsEnabled ? "on" : "off", settings.NotificationSeconds, settings.TopicMaxOutputTokens);
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
