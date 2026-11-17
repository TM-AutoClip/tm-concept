using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Components;
using TMConcept.Core.Hardware;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Settings → Local AI (0.1.20): the app's own Ollama copy and its models. Shows this computer's graphics card class, whether
/// "Ollama (TM Concept)" is installed (and whether its server runs), and the recommended models of every card class with their size, a green
/// (fits this card) or red (does not fit / processor) mark and an orange license mark when a license limits commercial use. Install and
/// Download go through <see cref="ComponentInstallFlow"/>: nothing is downloaded before the user confirmed the download list, the install
/// window shows the progress and can cancel (a model's pull goes on from where it stopped the next time). Installed models appear in the
/// model pickers of every text step; removing them is the storage manager's job. The user's own Ollama is never touched.
/// </summary>
public sealed partial class LocalAiSettingsViewModel(ComponentInstallFlow install, IAppOllamaRuntime runtime, IAppOllamaServer server, AppModelStore store,
    AppModelRegistry registry, IGpuInfo gpu, Localizer text, IActivityLog activity, ILogger<LocalAiSettingsViewModel> logger) : SettingsSectionBase
{
    private const double Gibibyte = 1024.0 * 1024 * 1024;

    /// <inheritdoc />
    public override string TitleKey => "settings.localai";

    /// <inheritdoc />
    public override int Order => 12;

    /// <summary>The recommended models per card class (this card's class first).</summary>
    public ObservableCollection<AppModelGroup> Groups { get; } = [];

    /// <summary>"Graphics card: … · 6 GB class" or "No usable graphics card…".</summary>
    [ObservableProperty]
    public partial string CardLine { get; set; } = string.Empty;

    /// <summary>"Ollama (TM Concept) 0.40.16 · installed" or "not installed · about 1.6 GB".</summary>
    [ObservableProperty]
    public partial string RuntimeLine { get; set; } = string.Empty;

    /// <summary>Whether the server runs now and on which port.</summary>
    [ObservableProperty]
    public partial string ServerLine { get; set; } = string.Empty;

    /// <summary>The program is installed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallRuntime))]
    public partial bool RuntimeReady { get; set; }

    /// <summary>The server runs (its Stop button shows).</summary>
    [ObservableProperty]
    public partial bool ServerRunning { get; set; }

    /// <summary>An install or a download runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Idle), nameof(CanInstallRuntime))]
    public partial bool Busy { get; set; }

    /// <summary>Not busy.</summary>
    public bool Idle => !Busy;

    /// <summary>The program can be installed (supported here, not installed, nothing running).</summary>
    public bool CanInstallRuntime => !RuntimeReady && !Busy && runtime.Supported;

    /// <inheritdoc />
    public override Task ActivateAsync()
    {
        Load();
        return Task.CompletedTask;
    }

    /// <summary>Reads the card, the program and the models again.</summary>
    [RelayCommand]
    public void Refresh() => Load();

    /// <summary>Installs the program after the user confirmed its download.</summary>
    [RelayCommand]
    public Task InstallRuntimeAsync() => EnsureAsync(ComponentCodes.AppOllama);

    /// <summary>Downloads a model (and the program first when it is missing) after the user confirmed the download list.</summary>
    [RelayCommand]
    public Task DownloadAsync(AppModelRowViewModel? row) =>
        row is null ? Task.CompletedTask : EnsureAsync(ComponentCodes.AppOllamaModelPrefix + row.Tag);

    /// <summary>Ends the app's own server now (it starts again when a model is used).</summary>
    [RelayCommand]
    public async Task StopServerAsync()
    {
        await server.StopAsync(CancellationToken.None).ConfigureAwait(true);
        Load();
    }

    private async Task EnsureAsync(string code)
    {
        if (Busy)
        {
            return;
        }

        Busy = true;
        try
        {
            var outcome = await install.EnsureAsync([code], "install.purpose.localai", null, CancellationToken.None).ConfigureAwait(true);
            registry.Refresh();
            Status = outcome.Ready ? text["localai.done"] : outcome.Reason;
            if (outcome.Ready)
            {
                activity.Add(ActivityLevel.Info, Status);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "Local AI: {Code} could not be installed", code);
            Status = text.Format("install.failed", ex.Message);
            activity.Add(ActivityLevel.Error, Status);
        }
        finally
        {
            Busy = false;
        }

        Load();
    }

    private void Load()
    {
        var report = gpu.Detect();
        var onCard = AppOllamaModelSource.OnCard(report);
        var card = onCard ? report.Largest() : null;
        var cardBytes = card?.VideoMemoryBytes;
        var tier = onCard ? VramTiers.ForCard(cardBytes) : VramTier.None;
        CardLine = card is null
            ? text["localai.card.none"]
            : cardBytes is > 0
                ? text.Format("localai.card", card.Name, Gigabytes(cardBytes.Value), tier == VramTier.None ? text["localai.card.small"] : text.Format("localai.tier", (int)tier))
                : text.Format("localai.card.unknown", card.Name);

        RuntimeReady = runtime.Ready;
        RuntimeLine = !runtime.Supported
            ? text["localai.runtime.unsupported"]
            : runtime.Ready
                ? text.Format("localai.runtime.ready", AppOllamaDefaults.Name, runtime.Version)
                : text.Format("localai.runtime.missing", AppOllamaDefaults.Name, runtime.Version);
        ServerRunning = server.Running;
        ServerLine = server.Address is { } address
            ? text.Format("localai.server.running", address.Port)
            : text["localai.server.stopped"];

        Groups.Clear();
        var tiers = new List<VramTier>();
        if (tier != VramTier.None || !onCard)
        {
            tiers.Add(tier);
        }

        tiers.AddRange(VramTiers.All.Where(t => t != tier));
        foreach (var each in tiers)
        {
            var rows = AppModelAdvisor.For(each).Select(m => Row(m, onCard ? cardBytes : null)).ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            var title = each == VramTier.None ? text["localai.group.processor"] : text.Format("localai.group", (int)each);
            Groups.Add(new AppModelGroup(each == tier ? text.Format("localai.group.yours", title) : title, each == tier, rows));
        }
    }

    private AppModelRowViewModel Row(AppModelInfo model, long? cardBytes)
    {
        var fit = AppModelAdvisor.Fit(model, cardBytes);
        var need = Gigabytes(AppModelAdvisor.CardBytes(model));
        var fitText = fit switch
        {
            AppModelFit.Fits => text.Format("localai.fit.fits", need),
            AppModelFit.TooLarge => text.Format("localai.fit.large", need),
            _ => text["localai.fit.processor"],
        };
        var license = text.Has("license." + model.License) ? text["license." + model.License] : model.License;
        return new AppModelRowViewModel(model, (model.Bytes / 1e9).ToString("0.0", text.Culture) + " GB", fitText, fit, license, store.IsInstalled(model.Tag));
    }

    private string Gigabytes(long bytes) => (bytes / Gibibyte).ToString("0.#", text.Culture);
}
