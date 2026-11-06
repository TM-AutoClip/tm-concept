using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Engines;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Startup;
using TMConcept.Engines.Components;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The step table shared by Settings → Models (the general defaults) and the Profile page (a profile's overrides; every step starts with
/// "Use the general setting"). Saving installs what a changed step needs first: licenses (Remotion, Supertonic) are asked, then ONE
/// confirmation lists every download, the install runs with progress, and only a step whose needs are ready afterwards is saved; a step
/// that was declined or failed keeps its old value and the reason is shown and logged. A paid model is never chosen automatically.
/// </summary>
public sealed partial class StepSettingsViewModel(IModelCatalog catalog, IStepModelStore steps, IEnumerable<IVideoEngine> engines, SpeechService speech,
    ComponentInstallFlow installs, AppSession session, IConfirmDialog confirm, IGpuInfo gpu, IAppPaths paths, ShellLauncher shell, Localizer text,
    ILogger<StepSettingsViewModel> logger) : ObservableObject
{
    private readonly IReadOnlyList<IVideoEngine> _engines = engines.OrderBy(e => e.Kind).ToList();
    private Guid? _profile;
    private string _language = ContentLanguages.English;

    /// <summary>The steps.</summary>
    public ObservableCollection<StepModelRowViewModel> Rows { get; } = [];

    /// <summary>Saving or installing.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>Install progress 0–100.</summary>
    [ObservableProperty]
    public partial double Percent { get; set; }

    /// <summary>What is being installed.</summary>
    [ObservableProperty]
    public partial string Progress { get; set; } = string.Empty;

    /// <summary>Result of the last save.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>A sample is being spoken.</summary>
    [ObservableProperty]
    public partial bool Listening { get; set; }

    /// <summary>Shows the general defaults (<paramref name="profileId"/> null) or a profile's overrides, for voices of <paramref name="language"/>.</summary>
    public async Task LoadAsync(Guid? profileId, string language)
    {
        _profile = profileId;
        _language = language;
        // A Node.js on the PATH counts as installed only once it was looked at (once per run): before any note says "not installed".
        await installs.Planner.RefreshAllAsync(CancellationToken.None).ConfigureAwait(true);
        var saved = await steps.ListAsync(profileId, CancellationToken.None).ConfigureAwait(true);
        var general = profileId is null ? saved : await steps.ListAsync(null, CancellationToken.None).ConfigureAwait(true);
        Rows.Clear();
        Status = string.Empty;
        foreach (var step in PipelineSteps.All)
        {
            var row = Row(step, general);
            var choice = saved.FirstOrDefault(s => s.Step == step.Code);
            var options = StepModelResolver.Options(choice);
            row.Selected = Select(row, choice);
            row.Effort = row.Efforts.FirstOrDefault(e => e.Value == options.Effort) ?? row.Efforts.FirstOrDefault();
            row.Engine = row.Engines.FirstOrDefault(e => e.Kind == options.Engine) ?? row.Engines.FirstOrDefault();
            row.Speed = (decimal)Math.Clamp(options.Speed, ProfileLimits.SpeedMin, ProfileLimits.SpeedMax);
            FillVoices(row, options.Voice);
            row.Saved = Key(row);
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
            await NoteAsync(row).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Saves the changed steps and installs what a step needs and this computer does not have: a changed step, and an unchanged one whose
    /// component was removed meanwhile (or never finished). True when every such step was saved (or is ready now). What is saved is what
    /// the rows showed when Save was pressed (the rows are locked while it runs).
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        if (Busy)
        {
            return false;
        }

        Busy = true;
        Status = string.Empty;
        var skipped = new List<(StepModelRowViewModel Row, string Reason)>();
        try
        {
            await installs.Planner.RefreshAllAsync(CancellationToken.None).ConfigureAwait(true);
            var todo = Rows.Where(r => Key(r) != r.Saved || NeedsInstall(r)).ToList();
            var wanted = todo.ToDictionary(r => r, r => (Choice: Desired(r), Key: Key(r), Label: Describe(r), Requires: Requirements(r)));
            foreach (var row in todo)
            {
                if (!await LicensesAsync(row).ConfigureAwait(true))
                {
                    skipped.Add((row, text["models.license_declined"]));
                }
            }

            var pending = todo.Where(r => skipped.All(s => s.Row != r)).ToList();
            var codes = pending.SelectMany(r => wanted[r].Requires).Distinct(StringComparer.Ordinal).ToList();
            var reason = text["install.not_ready"];
            if (codes.Count > 0)
            {
                Percent = 0;
                var outcome = await installs.EnsureAsync(codes, "install.purpose.save", InstallProgress(), CancellationToken.None).ConfigureAwait(true);
                Progress = string.Empty;
                if (!outcome.Ready)
                {
                    reason = outcome.Reason;
                }
            }

            foreach (var row in pending)
            {
                var want = wanted[row];
                if (!want.Requires.All(c => installs.Planner.Find(c) is not { } component || component.Ready))
                {
                    skipped.Add((row, reason));
                }
                else if (want.Key != row.Saved)
                {
                    await StoreAsync(row, want).ConfigureAwait(true);
                }
                else
                {
                    // Unchanged, only installed again: the note no longer says "installed when you save".
                    await NoteAsync(row).ConfigureAwait(true);
                }
            }

            foreach (var (row, why) in skipped)
            {
                logger.LogWarning("Setting not saved: {Scope} step {Step} needs something that is not installed or a license that was not accepted",
                    _profile is null ? "general" : "profile", row.Step.Code);
                await NoteAsync(row).ConfigureAwait(true);
                row.Note = why;
                row.NoteIsWarning = true;
            }

            Status = skipped.Count == 0
                ? text["common.saved"]
                : text.Format("models.not_saved", string.Join(" · ", skipped.Select(s => s.Row.Title)));
            return skipped.Count == 0;
        }
        finally
        {
            Progress = string.Empty;
            Busy = false;
        }
    }

    /// <summary>Speaks a sample sentence with the row's voice and opens it (an online voice is paid; a local one is installed first, after asking).</summary>
    [RelayCommand]
    public async Task ListenAsync(StepModelRowViewModel? row)
    {
        if (row?.Selected?.Model is not { } model || Listening || Busy)
        {
            return;
        }

        Listening = true;
        try
        {
            if (speech.EngineOf(model) is { IsLocal: true, Ready: false })
            {
                // The same flow as Save: the voice's terms, then one confirmation listing the downloads.
                if (!await LicensesAsync(row).ConfigureAwait(true))
                {
                    row.Note = text["models.license_declined"];
                    row.NoteIsWarning = true;
                    return;
                }

                Busy = true;
                InstallOutcome outcome;
                try
                {
                    Percent = 0;
                    outcome = await installs.EnsureAsync(Requirements(row), "install.purpose.listen", InstallProgress(), CancellationToken.None).ConfigureAwait(true);
                }
                finally
                {
                    Progress = string.Empty;
                    Busy = false;
                }

                if (!outcome.Ready)
                {
                    logger.LogInformation("Listen not started: the local voice is not installed");
                    row.Note = outcome.Reason;
                    row.NoteIsWarning = true;
                    return;
                }

                await NoteAsync(row).ConfigureAwait(true);
            }

            var setup = await speech.SetupAsync(Desired(row) ?? new StepModelChoice(), CancellationToken.None).ConfigureAwait(true);
            var wav = Path.Combine(paths.WorkDirectory, "listen", "sample.wav");
            await speech.SynthesizeAsync(setup, _language, text[_language == ContentLanguages.Turkish ? "speech.sample.tr" : "speech.sample.en"], wav,
                CancellationToken.None).ConfigureAwait(true);
            shell.OpenPath(wav);
        }
        catch (Exception ex) when (ex is SpeechException or IOException or UnauthorizedAccessException or Core.Providers.ProviderException)
        {
            logger.LogWarning(ex, "Listen failed");
            row.Note = text.Format("speech.failed", ex.Message);
            row.NoteIsWarning = true;
        }
        finally
        {
            Listening = false;
        }
    }

    private StepModelRowViewModel Row(PipelineStep step, IReadOnlyList<StepModelChoice> general)
    {
        var recommended = step.Recommended.Select(r => catalog.All.FirstOrDefault(m => m.Provider + "/" + m.Id == r)?.Name ?? r);
        var row = new StepModelRowViewModel(step, text["step." + step.Code], text["step." + step.Code + ".purpose"],
            text.Format("models.recommended", string.Join(" · ", recommended)), text["models.effort.auto"]);
        if (_profile is not null)
        {
            row.Options.Add(new StepOption(StepOptionKind.Inherit, null, text.Format("models.inherit", GeneralLabel(step, general))));
        }

        if (_profile is null || step.Optional)
        {
            row.Options.Add(new StepOption(StepOptionKind.None, null, step.Optional ? text["step." + step.Code + ".off"] : text["models.none"]));
        }

        foreach (var model in catalog.With(step.Capability).Where(m => Usable(step, m))
            .OrderBy(m => m.Provider == ModelProviders.Local ? 0 : 1).ThenBy(m => m.Provider, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal))
        {
            row.Options.Add(new StepOption(StepOptionKind.Model, model, Label(model)));
        }

        foreach (var engine in _engines)
        {
            row.Engines.Add(new EngineOption(engine.Kind, engine.Name + " · " + text[engine.Experimental ? "engines.tag.experimental"
                : engine.NeedsLicenseConsent ? "engines.tag.license" : "engines.tag.default"]));
        }

        return row;
    }

    private StepOption Select(StepModelRowViewModel row, StepModelChoice? choice)
    {
        if (choice is null)
        {
            return row.Options[0];
        }

        if (StepModelResolver.IsOff(choice))
        {
            return row.Options.FirstOrDefault(o => o.Kind == StepOptionKind.None) ?? row.Options[0];
        }

        if (row.Options.FirstOrDefault(o => o.Model is { } m && m.Provider == choice.Provider && m.Id == choice.Model) is { } found)
        {
            return found;
        }

        logger.LogWarning("The saved model of the step {Step} is not in the catalog any more; the step shows no model", row.Step.Code);
        return row.Options.FirstOrDefault(o => o.Kind == StepOptionKind.None) ?? row.Options[0];
    }

    private bool Usable(PipelineStep step, CatalogModel model) =>
        step.Capability != ModelCapability.Speech || speech.EngineOf(model) is { } engine && engine.Supports(_language);

    private string GeneralLabel(PipelineStep step, IReadOnlyList<StepModelChoice> general)
    {
        var choice = general.FirstOrDefault(s => s.Step == step.Code);
        if (StepModelResolver.IsOff(choice))
        {
            return step.Optional ? text["models.off_short"] : text["models.none_short"];
        }

        return catalog.Find(choice!.Provider, choice.Model)?.Name ?? choice.Model;
    }

    private string Label(CatalogModel model)
    {
        if (model.Provider == ModelProviders.Local)
        {
            return model.Name + " · " + text["models.local_free"] + (model.MinVideoMemoryGb > 0 ? " · " + text.Format("models.min_memory", model.MinVideoMemoryGb) : string.Empty);
        }

        var price = model.Capabilities.HasFlag(ModelCapability.Image)
            ? text.Usd(model.ImageUsd) + "/img"
            : model.Capabilities.HasFlag(ModelCapability.Speech)
                ? text.Usd(model.SpeechUsdPerMillionCharacters) + "/1M chr"
                : string.Create(CultureInfo.InvariantCulture, $"{model.InputUsd:0.##} / {model.OutputUsd:0.##} $");
        return model.Name + " · " + model.Provider + " · " + price;
    }

    private void FillVoices(StepModelRowViewModel row, string voice)
    {
        row.Voices.Clear();
        if (!row.IsVoiceStep || row.Selected?.Model is not { } model || speech.EngineOf(model) is not { } engine)
        {
            row.Voice = null;
            return;
        }

        foreach (var item in engine.Voices(_language))
        {
            row.Voices.Add(new ChoiceOption(item.Id, item.Label));
        }

        row.Voice = row.Voices.FirstOrDefault(v => v.Value == voice) ?? row.Voices.FirstOrDefault();
    }

    private async void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not StepModelRowViewModel row || e.PropertyName is not (nameof(StepModelRowViewModel.Selected) or nameof(StepModelRowViewModel.Engine)))
        {
            return;
        }

        try
        {
            if (e.PropertyName == nameof(StepModelRowViewModel.Selected))
            {
                FillVoices(row, row.Voice?.Value ?? string.Empty);
            }

            await NoteAsync(row).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The note of the step {Step} could not be shown", row.Step.Code);
        }
    }

    /// <summary>The note under a step: what an off step means, the engine's license, the voice's terms, graphics card memory, a pending install.</summary>
    private async Task NoteAsync(StepModelRowViewModel row)
    {
        row.NoteIsWarning = false;
        var selected = row.Selected;
        if (selected is null || selected.Kind == StepOptionKind.Inherit)
        {
            row.Note = string.Empty;
            return;
        }

        if (selected.Model is not { } model)
        {
            row.Note = row.Step.Optional ? text["step." + row.Step.Code + ".off.note"] : string.Empty;
            row.NoteIsWarning = row.IsVoiceStep;
            return;
        }

        var parts = new List<string>();
        var install = false;
        if (row.IsEngineStep && _engines.FirstOrDefault(e => e.Kind == row.Engine?.Kind) is { } engine)
        {
            parts.Add(text[engine.Experimental ? "engines.experimental" : engine.NeedsLicenseConsent ? "engines.license_short" : "engines.default"]);
            install = !engine.Status().Ready;
        }

        if (row.IsVoiceStep)
        {
            parts.Add(text[model.Provider == ModelProviders.Local ? "speech.supertonic.note" : "speech.online.note"]);
        }

        if (model.MinVideoMemoryGb > 0)
        {
            var memory = await gpu.VideoMemoryBytesAsync(CancellationToken.None).ConfigureAwait(true);
            var gigabytes = memory is { } bytes ? Math.Round(bytes / (1024.0 * 1024 * 1024), 1) : 0;
            parts.Add(text.Format("images.memory", model.MinVideoMemoryGb));
            parts.Add(memory is null ? text["images.memory.unknown"]
                : gigabytes + 0.25 < model.MinVideoMemoryGb ? text.Format("images.memory.low", gigabytes.ToString("0.#", text.Culture))
                : text.Format("images.memory.ok", gigabytes.ToString("0.#", text.Culture)));
            row.NoteIsWarning = memory is null || gigabytes + 0.25 < model.MinVideoMemoryGb;
        }

        install |= NeedsInstall(row);
        if (install)
        {
            parts.Add(text["models.installs_on_save"]);
        }

        row.Note = string.Join(" ", parts);
    }

    /// <summary>Asks for the licenses a row needs and has not got yet (Remotion; the Supertonic voice's OpenRAIL-M terms); false when declined.</summary>
    private async Task<bool> LicensesAsync(StepModelRowViewModel row)
    {
        var settings = session.Settings;
        if (row.Selected?.Model is not { } model)
        {
            return true;
        }

        if (row.IsEngineStep && _engines.FirstOrDefault(e => e.Kind == row.Engine?.Kind) is { NeedsLicenseConsent: true } && !settings.RemotionLicenseAccepted)
        {
            var license = await confirm.AskAsync(text["engines.license.title"], text["engines.license.message"], text["engines.license.accept"], danger: false)
                .ConfigureAwait(true);
            if (!license.Confirmed)
            {
                return false;
            }

            settings.RemotionLicenseAccepted = true;
            await session.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);
            logger.LogInformation("Setting changed: the Remotion license was accepted");
        }

        if (model.Provider == ModelProviders.Local && model.Id == SpeechEngines.Supertonic && !settings.SupertonicLicenseAccepted)
        {
            var license = await confirm.AskAsync(text["speech.supertonic.license.title"], text["speech.supertonic.license.message"], text["engines.license.accept"],
                danger: false).ConfigureAwait(true);
            if (!license.Confirmed)
            {
                return false;
            }

            settings.SupertonicLicenseAccepted = true;
            await session.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);
            logger.LogInformation("Setting changed: the Supertonic 3 terms were accepted");
        }

        return true;
    }

    /// <summary>The components a row needs: a local model's own, and the engine when a video code model is chosen.</summary>
    private static IReadOnlyList<string> Requirements(StepModelRowViewModel row)
    {
        if (row.Selected?.Model is not { } model)
        {
            return [];
        }

        var codes = new List<string>(model.Requires);
        if (row.IsEngineStep && row.Engine is { } engine)
        {
            codes.Add(EngineComponent.CodeOf(engine.Kind));
        }

        return codes;
    }

    private StepModelChoice? Desired(StepModelRowViewModel row)
    {
        var options = new StepOptions
        {
            Effort = row.Effort?.Value ?? string.Empty,
            Engine = row.Engine?.Kind ?? EngineKind.HyperFrames,
            Voice = row.Voice?.Value ?? string.Empty,
            Speed = (double)Math.Clamp(row.Speed ?? 1, (decimal)ProfileLimits.SpeedMin, (decimal)ProfileLimits.SpeedMax),
        };
        var choice = new StepModelChoice { Step = row.Step.Code, ProfileId = _profile, OptionsJson = StepModelResolver.OptionsJson(options) };
        switch (row.Selected?.Kind)
        {
            case StepOptionKind.Model:
                choice.Provider = row.Selected.Model!.Provider;
                choice.Model = row.Selected.Model.Id;
                return choice;
            case StepOptionKind.None when _profile is not null:
                choice.Provider = ModelProviders.Off;
                return choice;
            case StepOptionKind.None when row.IsEngineStep && options.Engine != EngineKind.HyperFrames:
                // No code model yet, but an engine other than the default: the engine choice is kept.
                return choice;
            default:
                return null;
        }
    }

    private async Task StoreAsync(StepModelRowViewModel row, (StepModelChoice? Choice, string Key, string Label, IReadOnlyList<string> Requires) want)
    {
        if (want.Choice is null)
        {
            await steps.ClearAsync(row.Step.Code, _profile, CancellationToken.None).ConfigureAwait(true);
        }
        else
        {
            await steps.SaveAsync(want.Choice, CancellationToken.None).ConfigureAwait(true);
        }

        row.Saved = want.Key;
        logger.LogInformation("Setting changed: {Scope} step {Step} uses {Model}", _profile is null ? "general" : "profile", row.Step.Code, want.Label);
    }

    /// <summary>What a row will be saved as, for the log (no personal data: model codes, effort, engine).</summary>
    private static string Describe(StepModelRowViewModel row)
    {
        var model = row.Selected switch
        {
            { Kind: StepOptionKind.Inherit } => "the general setting",
            { Model: { } m } => m.Provider + "/" + m.Id,
            _ => row.Step.Optional ? "nothing (off)" : "no model",
        };
        return model + " (effort " + (row.Effort?.Value is { Length: > 0 } effort ? effort : "automatic") + ", engine "
            + (row.IsEngineStep ? row.Engine?.Kind.ToString() ?? "-" : "-") + ")";
    }

    /// <summary>Whether something the row needs is not on this computer (a component removed meanwhile, or a half-finished install).</summary>
    private bool NeedsInstall(StepModelRowViewModel row) => Requirements(row).Any(c => installs.Planner.Find(c) is { Ready: false });

    private Progress<ToolProgress> InstallProgress() => new(p =>
    {
        Percent = Math.Round(p.Share * 100);
        Progress = p.Text;
    });

    private static string Key(StepModelRowViewModel row) => string.Join('|', row.Selected?.Kind.ToString() ?? string.Empty,
        row.Selected?.Model is { } m ? m.Provider + "/" + m.Id : string.Empty, row.Effort?.Value ?? string.Empty,
        row.IsEngineStep ? row.Engine?.Kind.ToString() ?? string.Empty : string.Empty,
        row.IsVoiceStep ? row.Voice?.Value ?? string.Empty : string.Empty,
        row.IsVoiceStep ? (row.Speed ?? 1).ToString("0.00", CultureInfo.InvariantCulture) : string.Empty);
}
