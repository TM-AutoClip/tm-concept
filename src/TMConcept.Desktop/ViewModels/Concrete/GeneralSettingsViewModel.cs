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
/// components (read-only list with sizes of what is on disk, an incomplete one marked; Remove after a confirmation, deleted for good
/// like Settings → Storage; a ready engine can
/// render its sample; on Linux the apt command for Chrome's missing libraries). Components are installed when a process that needs them starts (after one confirmation).
/// Image acceleration (per computer): which stable-diffusion.cpp build runs the local image models, with what Automatic picks here (the
/// card found and its memory); a change installs that build at once when a local image model is already on disk (one confirmation).
/// Text recognition (0.1.27, per computer, shown on Windows): whether the local OCR of scanned pages may use the graphics card when it is free.
/// </summary>
public sealed partial class GeneralSettingsViewModel(AppSession session, Localizer text, UiPreferencesFile preferences, INotifier notifier,
    ComponentPlanner components, IToolPlatform platform, Engines.Engines.SampleRender sample, IConfirmDialog confirm, ShellLauncher shell,
    IActivityLog activity, PreferencesImageAcceleration acceleration, Engines.Images.ImageBackendSelector backends, ComponentInstallFlow installs,
    ILogger<GeneralSettingsViewModel> logger, ComponentReinstaller reinstaller, Updates.AppUpdater updater, IAppUpdateDialog updateDialog,
    DiskSpace.IDiskCleaner cleaner, WhatsNewCheck whatsNew, Updates.IAppEdition edition, Updates.EditionInstaller editionInstaller,
    IShortcutWriter shortcuts, PreferencesOcrAcceleration ocrAcceleration, TMConcept.Documents.Ocr.OcrGpuModel ocrCard, Core.Models.IGpuCoordinator gpu)
    : SettingsSectionBase
{
    /// <summary>The "Text recognition" choices (0.1.27).</summary>
    public ObservableCollection<ChoiceOption> OcrAccelerationOptions { get; } = [];

    /// <summary>The chosen "Text recognition" setting.</summary>
    [ObservableProperty]
    public partial ChoiceOption? OcrAccelerationChoice { get; set; }

    /// <summary>Whether "Text recognition" is shown (the graphics card is used only on Windows).</summary>
    public bool ShowsOcrAcceleration => OperatingSystem.IsWindows();

    /// <summary>"Edition: installed · 0.1.26" or "Edition: portable · 0.1.26".</summary>
    public string EditionLine => text.Format(edition.Kind == Updates.AppEditionKind.Installed ? "edition.installed" : "edition.portable", updater.Current);

    /// <summary>The installed edition's folder (shown read-only); empty for the portable program.</summary>
    public string InstallFolder => edition.InstallRoot is { } root ? text.Format("edition.folder", root) : string.Empty;

    /// <summary>Whether the install folder is shown.</summary>
    public bool HasInstallFolder => edition.InstallRoot is not null;

    /// <summary>"Install TM Concept" is offered (the published portable program on Windows).</summary>
    public bool CanInstallEdition => editionInstaller.Available;

    /// <summary>"Recreate shortcuts" is offered (the installed edition on Windows).</summary>
    public bool CanRecreateShortcuts => edition.Kind == Updates.AppEditionKind.Installed && shortcuts.Supported;

    /// <summary>What the install or the shortcuts did last (progress, done, why not).</summary>
    [ObservableProperty]
    public partial string EditionStatus { get; set; } = string.Empty;

    /// <summary>The install is running.</summary>
    [ObservableProperty]
    public partial bool InstallingEdition { get; set; }

    /// <summary>
    /// Installs TM Concept from the portable program after a confirmation: Setup of this version is downloaded, checked against the release
    /// notes, started, and this copy closes. The portable file stays.
    /// </summary>
    [RelayCommand]
    public async Task InstallEditionAsync()
    {
        if (InstallingEdition || !CanInstallEdition)
        {
            return;
        }

        var answer = await confirm.AskAsync(text["edition.offer.title"], text["edition.offer.message"], text["edition.offer.install"], danger: false)
            .ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        InstallingEdition = true;
        EditionStatus = text["edition.install.downloading"];
        try
        {
            var progress = new Progress<ToolProgress>(p => EditionStatus = text.Format("edition.install.progress", Math.Round(p.Share * 100)));
            await Task.Run(() => editionInstaller.InstallAsync(progress, CancellationToken.None)).ConfigureAwait(true);
            EditionStatus = text["edition.install.started"];
        }
        catch (Updates.AppUpdateException ex)
        {
            logger.LogWarning(ex, "TM Concept was not installed ({Code})", ex.Code);
            EditionStatus = text["edition.install.fail." + ex.Code];
        }
        catch (ToolInstallException ex)
        {
            logger.LogError(ex, "The installer could not be downloaded");
            EditionStatus = text.Format("edition.install.failed", ComponentInstallFlow.Reason(text, ex));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or System.ComponentModel.Win32Exception
            or InvalidOperationException)
        {
            logger.LogError(ex, "TM Concept could not be installed; the portable copy keeps running");
            EditionStatus = text.Format("edition.install.failed", ex.Message);
        }
        finally
        {
            InstallingEdition = false;
        }
    }

    /// <summary>Writes the desktop and Start menu shortcuts of the installed edition again (one the user deleted comes back).</summary>
    [RelayCommand]
    public void RecreateShortcuts()
    {
        if (!CanRecreateShortcuts || Environment.ProcessPath is not { Length: > 0 } program)
        {
            return;
        }

        try
        {
            // The id the window gives itself (TaskbarIcon) and Setup gives its shortcuts (vpk --aumid): a pinned button and the window group.
            var written = shortcuts.Write(program, TaskbarIcon.AppId);
            EditionStatus = text.Format("edition.shortcuts.done", written.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            logger.LogError(ex, "The shortcuts could not be written");
            EditionStatus = text.Format("edition.shortcuts.failed", ex.Message);
        }
    }

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

    /// <summary>Opens "What's new": the notes of every version up to the running one, newest first, from the notes built into the app.</summary>
    [RelayCommand]
    public Task WhatsNewAsync() => whatsNew.ShowAllAsync(updater.Current);

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
        OcrAccelerationOptions.Clear();
        foreach (var value in Enum.GetValues<OcrAcceleration>())
        {
            OcrAccelerationOptions.Add(new ChoiceOption(value.ToString(), text[OcrAccelerationKey(value)]));
        }

        OcrAccelerationChoice = OcrAccelerationOptions.FirstOrDefault(o => o.Value == ocrAcceleration.Value.ToString());
        _loading = false;
        // The first call looks at the graphics cards (native calls; on Linux maybe a short nvidia-smi run): off the UI thread.
        AccelerationNote = Detected(await Task.Run(backends.Automatic).ConfigureAwait(true));
        NotificationsEnabled = session.Settings.NotificationsEnabled;
        NotificationSeconds = session.Settings.NotificationSeconds;
        TopicMaxOutputTokens = Core.Concepts.TopicSuggester.ClampOutput(session.Settings.TopicMaxOutputTokens);
        await RefreshInstalledAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Removes a component after a confirmation (it is downloaded again when a setting needs it), through the storage manager
    /// (<see cref="DiskSpace.IDiskCleaner"/>).
    /// </summary>
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
            // The same way as Settings → Storage: not while work runs, the voice workers ended and the card freed first, deleted for good.
            var outcome = await cleaner.RemoveAsync([cleaner.ItemOf(row.Component)], CancellationToken.None).ConfigureAwait(true);
            if (outcome.Busy)
            {
                Status = text["storage.busy"];
                activity.Add(ActivityLevel.Warning, Status);
            }
            else if (outcome.Failed.Count > 0)
            {
                Status = text.Format("components.remove_failed", row.Name, outcome.Failed[0].Reason);
                activity.Add(ActivityLevel.Error, Status);
            }
            else
            {
                activity.Add(ActivityLevel.Info, text.Format("components.removed", row.Name));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
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
    /// "Reinstall components from scratch" (the same as after a new app version): after a confirmation (which shows the folder's size) what
    /// was installed is installed again into a new folder and the old one is deleted for good. Not while work runs: its components are in use.
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

        var size = StorageSettingsViewModel.Size(await Task.Run(() => Core.DiskSpace.DiskSizes.Of(reinstaller.Folder)).ConfigureAwait(true), text.Culture);
        var answer = await confirm.AskAsync(text["components.reinstall.title"], text.Format("components.reinstall.message", reinstaller.Location(), size),
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

    /// <summary>The language key of a "Text recognition" value (0.1.27).</summary>
    internal static string OcrAccelerationKey(OcrAcceleration value) => value == OcrAcceleration.Processor
        ? "settings.ocr_acceleration.processor"
        : "settings.ocr_acceleration.automatic";

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

    partial void OnOcrAccelerationChoiceChanged(ChoiceOption? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        var chosen = Enum.GetValues<OcrAcceleration>().FirstOrDefault(v => v.ToString() == value.Value);
        if (chosen == ocrAcceleration.Value)
        {
            return;
        }

        ocrAcceleration.Change(chosen);
        logger.LogInformation("Setting changed: text recognition {Setting}", chosen);
        Status = text[chosen == OcrAcceleration.Processor ? "settings.ocr_acceleration.saved.processor" : "settings.ocr_acceleration.saved.automatic"];
        if (chosen == OcrAcceleration.Processor)
        {
            // The OCR's networks on the card give its memory back now, not when another model next asks for the card.
            _ = FreeOcrCardAsync();
        }
    }

    /// <summary>Closes the local OCR's networks on the graphics card (0.1.27, "Processor only"); a failure is logged.</summary>
    internal async Task FreeOcrCardAsync()
    {
        try
        {
            await ocrCard.CloseAsync(gpu, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "The text recognition networks could not be taken off the graphics card");
        }
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
