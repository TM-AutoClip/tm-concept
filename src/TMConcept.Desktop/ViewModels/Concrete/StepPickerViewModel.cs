using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Engines;
using TMConcept.Core.Hardware;
using TMConcept.Core.Images;
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
/// The model pickers of one page (Generate concepts, Concept → video, Profile): only the steps that page uses, shown with the selected
/// profile's last choices (a new profile starts with nothing paid chosen; the "Recommended" chips stay). There is no Save button: before the
/// page starts a process, <see cref="PrepareAsync"/> makes the choices ready and saves them for the profile — licenses (Remotion,
/// Supertonic) are asked, a provider without an API key is offered a key right there (tested; only a working key is kept), then ONE
/// confirmation lists every download, the install runs with progress, and only then the choices are saved and the process may start.
/// Anything declined, missing or failed stops the process before anything is paid for, with the reason on the page and in the log.
/// </summary>
public sealed partial class StepPickerViewModel(IModelCatalog catalog, IStepModelStore steps, StepModelResolver resolver, IEnumerable<IVideoEngine> engines,
    SpeechService speech, ComponentInstallFlow installs, AppSession session, IConfirmDialog confirm, IApiKeyDialog keyDialog, IGpuInfo gpu, IAppPaths paths,
    ShellLauncher shell, Localizer text, ILogger<StepPickerViewModel> logger) : ObservableObject
{
    /// <summary>The "saved" mark of a row whose saved choice is not offered any more: never equal to a row's key, so it is saved again.</summary>
    private const string StaleChoice = "\u0000stale";

    private readonly IReadOnlyList<IVideoEngine> _engines = engines.OrderBy(e => e.Kind).ToList();
    private Guid? _profile;
    private string _language = ContentLanguages.English;

    /// <summary>Raised when a choice changes (the page updates its estimate).</summary>
    public event EventHandler? Changed;

    /// <summary>The steps of the page.</summary>
    public ObservableCollection<StepModelRowViewModel> Rows { get; } = [];

    /// <summary>The profile whose choices are shown.</summary>
    public Guid? ProfileId => _profile;

    /// <summary>Preparing (keys, installs) or saving: the pickers are locked.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>Install progress 0–100.</summary>
    [ObservableProperty]
    public partial double Percent { get; set; }

    /// <summary>What is being installed.</summary>
    [ObservableProperty]
    public partial string Progress { get; set; } = string.Empty;

    /// <summary>Why the last <see cref="PrepareAsync"/> did not start the process (empty when it did).</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>A sample is being spoken.</summary>
    [ObservableProperty]
    public partial bool Listening { get; set; }

    /// <summary>
    /// How many step cards stand side by side (1 = a column). The video page shows its three steps side by side so the whole area fits
    /// without scrolling (two columns left the lists below too little room at 820 px).
    /// </summary>
    [ObservableProperty]
    public partial int Columns { get; set; } = 1;

    /// <summary>
    /// Compact cards: the purpose of a step is the title's tooltip instead of a line under it, and a note shows at most
    /// <see cref="CompactNoteLines"/> lines (the whole note is its tooltip).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPurpose), nameof(NoteLines))]
    public partial bool Compact { get; set; }

    /// <summary>Lines of a note on a compact card.</summary>
    public const int CompactNoteLines = 3;

    /// <summary>Whether the purpose line is shown under each title.</summary>
    public bool ShowPurpose => !Compact;

    /// <summary>Most lines of a note (0 = all).</summary>
    public int NoteLines => Compact ? CompactNoteLines : 0;

    /// <summary>Shows the steps <paramref name="stepCodes"/> with the saved choices of <paramref name="profileId"/>, voices of <paramref name="language"/>.</summary>
    public async Task LoadAsync(Guid? profileId, string language, IReadOnlyList<string> stepCodes)
    {
        ArgumentNullException.ThrowIfNull(stepCodes);
        _profile = profileId;
        _language = language;

        // A Node.js on the PATH counts as installed only once it was looked at (once per run): before any note says "not installed".
        await installs.Planner.RefreshAllAsync(CancellationToken.None).ConfigureAwait(true);
        var saved = profileId is { } id ? await steps.ListAsync(id, CancellationToken.None).ConfigureAwait(true) : [];
        foreach (var old in Rows)
        {
            old.PropertyChanged -= OnRowChanged;
        }

        Rows.Clear();
        Status = string.Empty;
        foreach (var step in PipelineSteps.All.Where(s => stepCodes.Contains(s.Code)))
        {
            var row = Row(step);
            var choice = saved.FirstOrDefault(s => s.Step == step.Code);
            var options = StepModelResolver.Options(choice);
            row.Selected = Select(row, choice, options, out var stale);
            row.Effort = row.Efforts.FirstOrDefault(e => e.Value == options.Effort) ?? row.Efforts.FirstOrDefault();
            row.Engine = row.Engines.FirstOrDefault(e => e.Kind == options.Engine) ?? row.Engines.FirstOrDefault();
            row.Speed = (decimal)Math.Clamp(options.Speed, ProfileLimits.SpeedMin, ProfileLimits.SpeedMax);
            FillVoices(row, options.Voice);
            if (!stale && row.IsVoiceStep && options.Voice.Length > 0 && row.Selected?.Model is not null && row.Voice is { } shown && shown.Value != options.Voice)
            {
                // The saved voice is not offered any more (a removed own voice): the first voice is shown, said and saved at the next prepare.
                logger.LogWarning("The saved voice of the narration is not offered any more; the first voice of the model is shown");
                row.Fallback = text.Format("speech.fallback.voice", shown.Label);
                stale = true;
            }

            // A saved model that is not offered any more (catalog, or a voice that does not speak the profile's language now) shows "none":
            // the row is not counted as saved, so the next prepare writes what the page shows and a process never uses the hidden choice.
            row.Saved = stale ? StaleChoice : Key(row);
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
            await NoteAsync(row).ConfigureAwait(true);
        }
    }

    /// <summary>The row of a step (null when the page does not show it).</summary>
    public StepModelRowViewModel? Row(string step) => Rows.FirstOrDefault(r => r.Step.Code == step);

    /// <summary>The model chosen for a step now (null = none / off).</summary>
    public CatalogModel? Model(string step) => Row(step)?.Selected?.Model;

    /// <summary>The choice a row shows now, as it would be saved (null = nothing chosen for a required step).</summary>
    public StepModelChoice? Choice(string step) => Row(step) is { } row ? Desired(row) : null;

    /// <summary>
    /// Makes the choices of <paramref name="needed"/> ready for a process and saves every row of the page for the profile. A required step
    /// without a model, a declined license, a provider without a working key (the user can add one in the dialog), a declined or failed
    /// install: false, with the reason in <see cref="Status"/> and the log; nothing was started or paid for. <paramref name="purposeKey"/>
    /// names what waits in the download confirmation; <paramref name="components"/> are installed in the same confirmation (ffmpeg for a video).
    /// </summary>
    public async Task<bool> PrepareAsync(IReadOnlyList<string> needed, string purposeKey, CancellationToken cancellationToken,
        IReadOnlyList<string>? components = null)
    {
        ArgumentNullException.ThrowIfNull(needed);
        if (Busy)
        {
            return false;
        }

        if (_profile is null)
        {
            Status = text["picker.no_profile"];
            return false;
        }

        Busy = true;
        Status = string.Empty;
        try
        {
            var rows = Rows.Where(r => needed.Contains(r.Step.Code)).ToList();
            foreach (var row in rows.Where(r => !r.Step.Optional && r.Selected?.Model is null))
            {
                row.Note = text["picker.choose_model"];
                row.NoteIsWarning = true;
                logger.LogWarning("Process not started: the step {Step} has no model", row.Step.Code);
                Status = text.Format("picker.not_started.model", row.Title);
                return false;
            }

            foreach (var row in rows)
            {
                if (!await LicensesAsync(row).ConfigureAwait(true))
                {
                    row.Note = text["models.license_declined"];
                    row.NoteIsWarning = true;
                    logger.LogInformation("Process not started: the license of the step {Step} was declined", row.Step.Code);
                    Status = text.Format("picker.not_started.license", row.Title);
                    return false;
                }
            }

            if (!await KeysAsync(rows, cancellationToken).ConfigureAwait(true))
            {
                return false;
            }

            var codes = (components ?? []).Concat(rows.SelectMany(Requirements)).Distinct(StringComparer.Ordinal).ToList();
            if (codes.Count > 0)
            {
                Percent = 0;
                var outcome = await installs.EnsureAsync(codes, purposeKey, InstallProgress(), cancellationToken).ConfigureAwait(true);
                Progress = string.Empty;
                foreach (var row in rows)
                {
                    await NoteAsync(row).ConfigureAwait(true);
                }

                if (!outcome.Ready)
                {
                    logger.LogWarning("Process not started: what the chosen models need is not installed");
                    Status = text.Format("picker.not_started.install", outcome.Reason);
                    return false;
                }
            }

            await SaveAsync().ConfigureAwait(true);
            return true;
        }
        finally
        {
            Progress = string.Empty;
            Busy = false;
        }
    }

    /// <summary>Saves the rows that changed since they were loaded or last saved (the profile's last choices).</summary>
    public async Task SaveAsync()
    {
        if (_profile is not { } profile)
        {
            return;
        }

        foreach (var row in Rows.Where(r => Key(r) != r.Saved))
        {
            var choice = Desired(row);
            if (choice is null)
            {
                await steps.ClearAsync(row.Step.Code, profile, CancellationToken.None).ConfigureAwait(true);
            }
            else
            {
                await steps.SaveAsync(choice, CancellationToken.None).ConfigureAwait(true);
            }

            row.Saved = Key(row);
            logger.LogInformation("Profile choice saved: step {Step} uses {Model}", row.Step.Code, Describe(row));
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
            if (speech.EngineOf(model) is { IsLocal: true, Ready: false } || model.Provider != ModelProviders.Local)
            {
                // The same way as a process: the voice's terms, a key for an online voice, one confirmation listing the downloads.
                if (!await PrepareRowAsync(row, "install.purpose.listen").ConfigureAwait(true))
                {
                    return;
                }
            }

            var choice = Desired(row) ?? new StepModelChoice();
            var setup = await speech.SetupAsync(choice, _language, CancellationToken.None).ConfigureAwait(true);
            var wav = Path.Combine(paths.WorkDirectory, "listen", "sample.wav");
            await speech.SynthesizeAsync(setup, _language, OutputLanguages.Of(_language).Sample, wav,
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

    private async Task<bool> PrepareRowAsync(StepModelRowViewModel row, string purposeKey)
    {
        if (!await LicensesAsync(row).ConfigureAwait(true))
        {
            row.Note = text["models.license_declined"];
            row.NoteIsWarning = true;
            return false;
        }

        Busy = true;
        try
        {
            if (!await KeysAsync([row], CancellationToken.None).ConfigureAwait(true))
            {
                row.Note = Status;
                row.NoteIsWarning = true;
                return false;
            }

            Percent = 0;
            var outcome = await installs.EnsureAsync(Requirements(row), purposeKey, InstallProgress(), CancellationToken.None).ConfigureAwait(true);
            if (!outcome.Ready)
            {
                logger.LogInformation("Listen not started: the local voice is not installed");
                row.Note = outcome.Reason;
                row.NoteIsWarning = true;
                return false;
            }
        }
        finally
        {
            Progress = string.Empty;
            Busy = false;
        }

        await NoteAsync(row).ConfigureAwait(true);
        return true;
    }

    /// <summary>
    /// The key gate: every online provider of the rows needs a working API key (a saved key that fails its free test does not count). A
    /// missing one is asked for in a dialog (tested there; only a key that works is saved); declined = the process does not start.
    /// </summary>
    private async Task<bool> KeysAsync(IReadOnlyList<StepModelRowViewModel> rows, CancellationToken cancellationToken)
    {
        var models = rows.Select(r => r.Selected?.Model).OfType<CatalogModel>().ToList();
        foreach (var provider in await resolver.ProvidersWithoutWorkingKeyAsync(models, cancellationToken).ConfigureAwait(true))
        {
            logger.LogWarning("The provider {Provider} has no working API key; one is asked for before the process starts", provider);
            if (!await keyDialog.AddKeyAsync(provider).ConfigureAwait(true))
            {
                logger.LogWarning("Process not started: no API key was added for {Provider}", provider);
                Status = text.Format("picker.not_started.key", ProviderName(provider));
                foreach (var row in rows.Where(r => r.Selected?.Model?.Provider == provider))
                {
                    row.Note = text.Format("picker.no_key", ProviderName(provider));
                    row.NoteIsWarning = true;
                }

                return false;
            }

            if (!resolver.CanTest(provider))
            {
                // No free test exists (fal): the key was saved untested; it is checked at its first use, and the page says so.
                foreach (var row in rows.Where(r => r.Selected?.Model?.Provider == provider))
                {
                    row.Note = text["keys.test.untested"];
                    row.NoteIsWarning = true;
                }
            }
        }

        return true;
    }

    private string ProviderName(string provider) => keyDialog.ProviderName(provider);

    private StepModelRowViewModel Row(PipelineStep step)
    {
        // Only what this page offers: a voice that does not speak the profile's language is not recommended either.
        // The default voice of the profile's language is suggested first.
        var suggested = step.Code == PipelineSteps.Speech
            ? step.Recommended.OrderBy(r => r == SpeechDefaults.ModelFor(_language) ? 0 : 1).ToList()
            : step.Recommended;
        var recommended = suggested
            .Select(r => (Code: r, Model: catalog.All.FirstOrDefault(m => m.Provider + "/" + m.Id == r)))
            .Where(r => r.Model is null || Usable(step, r.Model))
            .Select(r => r.Model?.Name ?? r.Code);
        var row = new StepModelRowViewModel(step, text["step." + step.Code], text["step." + step.Code + ".purpose"],
            text.Format("models.recommended", string.Join(" · ", recommended)), text["models.effort.auto"]);
        row.Options.Add(new StepOption(StepOptionKind.None, null, step.Optional ? text["step." + step.Code + ".off"] : text["models.none"]));
        if (step.Code == PipelineSteps.Images)
        {
            row.Options.Add(new StepOption(StepOptionKind.Samples, null, text["step.images.samples"]));
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

    private StepOption Select(StepModelRowViewModel row, StepModelChoice? choice, StepOptions options, out bool stale)
    {
        stale = false;
        if (StepModelResolver.IsOff(choice))
        {
            return choice is not null && options.ImageSource == ImageSources.Samples && row.Options.FirstOrDefault(o => o.Kind == StepOptionKind.Samples) is { } samples
                ? samples
                : row.Options[0];
        }

        if (row.Options.FirstOrDefault(o => o.Model is { } m && m.Provider == choice!.Provider && m.Id == choice.Model) is { } found)
        {
            return found;
        }

        stale = true;
        if (row.IsVoiceStep && row.Options.FirstOrDefault(o => o.Model is { } m && m.Provider + "/" + m.Id == SpeechDefaults.ModelFor(_language)) is { } fallback)
        {
            // A voice that does not speak the profile's language (the language was changed): the language's default voice instead, said on
            // the step and saved at the next prepare, so a process never uses the old choice.
            var old = catalog.All.FirstOrDefault(m => m.Provider == choice!.Provider && m.Id == choice.Model)?.Name ?? choice!.Model;
            logger.LogWarning("The saved voice {Provider}/{Model} does not speak {Language}; the default voice {Default} is shown", choice!.Provider, choice.Model,
                _language, fallback.Model!.Id);
            row.Fallback = text.Format("speech.fallback.language", old, OutputLanguages.NameIn(_language, text.Language), fallback.Model.Name);
            return fallback;
        }

        logger.LogWarning("The saved model of the step {Step} is not offered any more (catalog or content language); the step shows no model", row.Step.Code);
        return row.Options[0];
    }

    private bool Usable(PipelineStep step, CatalogModel model) =>
        step.Capability != ModelCapability.Speech || speech.EngineOf(model) is { } engine && engine.Supports(_language);

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
        if (sender is not StepModelRowViewModel row || e.PropertyName is not (nameof(StepModelRowViewModel.Selected) or nameof(StepModelRowViewModel.Engine)
            or nameof(StepModelRowViewModel.Effort) or nameof(StepModelRowViewModel.Voice)))
        {
            return;
        }

        try
        {
            if (e.PropertyName is nameof(StepModelRowViewModel.Selected) or nameof(StepModelRowViewModel.Voice) && row.Fallback.Length > 0)
            {
                // The user chose: the note about the replaced choice is not true any more.
                row.Fallback = string.Empty;
            }

            if (e.PropertyName == nameof(StepModelRowViewModel.Selected))
            {
                FillVoices(row, row.Voice?.Value ?? string.Empty);
            }

            Changed?.Invoke(this, EventArgs.Empty);
            if (e.PropertyName != nameof(StepModelRowViewModel.Effort))
            {
                await NoteAsync(row).ConfigureAwait(true);
            }
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
        if (selected is null)
        {
            row.Note = string.Empty;
            return;
        }

        if (selected.Kind == StepOptionKind.Samples)
        {
            row.Note = text["step.images.samples.note"];
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
            parts.Add(text[model.Provider != ModelProviders.Local ? "speech.online.note" : "speech." + model.Id + ".note"]);
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

        if (model.Provider != ModelProviders.Local && (await resolver.ProvidersWithoutKeyAsync([model], CancellationToken.None).ConfigureAwait(true)).Count > 0)
        {
            parts.Add(text.Format("picker.key_asked", ProviderName(model.Provider)));
        }

        install |= NeedsInstall(row);
        if (install)
        {
            parts.Add(text["models.installs_on_start"]);
        }

        if (row.Fallback.Length > 0)
        {
            parts.Insert(0, row.Fallback);
            row.NoteIsWarning = true;
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
            ImageSource = row.Selected?.Kind == StepOptionKind.Samples ? ImageSources.Samples : string.Empty,
        };
        var choice = new StepModelChoice { Step = row.Step.Code, ProfileId = _profile, OptionsJson = StepModelResolver.OptionsJson(options) };
        switch (row.Selected?.Kind)
        {
            case StepOptionKind.Model:
                choice.Provider = row.Selected.Model!.Provider;
                choice.Model = row.Selected.Model.Id;
                return choice;
            case StepOptionKind.Samples:
            case StepOptionKind.None when row.Step.Optional:
                choice.Provider = ModelProviders.Off;
                return choice;
            case StepOptionKind.None when row.IsEngineStep && options.Engine != EngineKind.HyperFrames:
                // No code model yet, but an engine other than the default: the engine choice is kept.
                return choice;
            default:
                return null;
        }
    }

    /// <summary>What a row will be saved as, for the log (no personal data: model codes, effort, engine).</summary>
    private static string Describe(StepModelRowViewModel row)
    {
        var model = row.Selected switch
        {
            { Kind: StepOptionKind.Samples } => "the sample images",
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
