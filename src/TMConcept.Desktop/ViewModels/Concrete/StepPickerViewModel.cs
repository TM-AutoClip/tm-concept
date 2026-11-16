using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Endpoints;
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
/// profile's last choices (a new profile starts with nothing paid chosen; the "Recommended" chips stay). There is no Save button: every
/// change the user makes is saved for the profile at once (0.1.17, the developer: a choice stays exactly as the user last chose it, also
/// after a page switch, a restart, a new key or a new server). A saved model that is not offered now (its server gone, a model removed) is
/// never replaced on its own: it is shown as unavailable with the reason and kept. Before the page starts a process,
/// <see cref="PrepareAsync"/> makes the choices ready — licenses (Remotion, Supertonic) are asked, a provider without an API key is offered
/// a key right there (tested; only a working key is kept), then ONE confirmation lists every download and the install runs with progress.
/// Anything declined, missing, unavailable or failed stops the process before anything is paid for, with the reason on the page and in the log.
/// </summary>
public sealed partial class StepPickerViewModel(IModelCatalog catalog, IStepModelStore steps, StepModelResolver resolver, IEnumerable<IVideoEngine> engines,
    SpeechService speech, VoiceSampleLibrary voiceSamples, ComponentInstallFlow installs, AppSession session, IConfirmDialog confirm, IApiKeyDialog keyDialog, Engines.Images.ImageBackendSelector backends, IAppPaths paths,
    ShellLauncher shell, Localizer text, ILogger<StepPickerViewModel> logger) : ObservableObject
{
    /// <summary>The "saved" mark of a row whose saved choice is not offered any more: never equal to a row's key, so it is saved again.</summary>
    private const string StaleChoice = "\u0000stale";

    private readonly IReadOnlyList<IVideoEngine> _engines = engines.OrderBy(e => e.Kind).ToList();
    private Guid? _profile;
    private string _language = ContentLanguages.English;
    private bool _languageFollowed;

    // The image build chosen when the page was loaded (the graphics card and its class), for the colour of each local image model.
    private ImageBackendChoice? _imageChoice;

    /// <summary>Raised when a choice changes (the page updates its estimate).</summary>
    public event EventHandler? Changed;

    /// <summary>The steps of the page, one card each.</summary>
    public ObservableCollection<StepModelRowViewModel> Rows { get; } = [];

    /// <summary>
    /// The choices on one line for the folded models bar (0.1.13, the developer: the pickers "take needless room"): "Code: Claude Sonnet
    /// 5.5 (medium) · Engine: HyperFrames · Images: off · Voice: Kokoro af_heart". Read in the current language.
    /// </summary>
    public string Summary => string.Join(" · ", Rows.SelectMany(SummaryParts));

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
    /// Compact cards: a note shows at most <see cref="CompactNoteLines"/> lines (the whole note is its tooltip). The purpose of a step is
    /// always the "?" next to its title, never a line under it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteLines))]
    public partial bool Compact { get; set; }

    /// <summary>Lines of a note on a compact card.</summary>
    public const int CompactNoteLines = 3;

    /// <summary>Most lines of a note (0 = all).</summary>
    public int NoteLines => Compact ? CompactNoteLines : 0;

    /// <summary>
    /// Shows the steps <paramref name="stepCodes"/> in the page's order with the saved choices of <paramref name="profileId"/>, voices of
    /// <paramref name="language"/>.
    /// </summary>
    public async Task LoadAsync(Guid? profileId, string language, IReadOnlyList<string> stepCodes)
    {
        ArgumentNullException.ThrowIfNull(stepCodes);
        _profile = profileId;
        _language = language;

        // A Node.js on the PATH counts as installed only once it was looked at (once per run): before any note says "not installed".
        await installs.Planner.RefreshAllAsync(CancellationToken.None).ConfigureAwait(true);

        // The user's own voices (a voice that copies a voice lists them): added or removed on the settings page meanwhile.
        await voiceSamples.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        var saved = profileId is { } id ? await steps.ListAsync(id, CancellationToken.None).ConfigureAwait(true) : [];

        // The graphics cards are looked at once per load (native calls; on Linux maybe a short nvidia-smi run): off the UI thread.
        _imageChoice = stepCodes.Contains(PipelineSteps.Images) ? await Task.Run(backends.Choose).ConfigureAwait(true) : null;
        foreach (var old in Rows)
        {
            old.PropertyChanged -= OnRowChanged;
        }

        if (!_languageFollowed)
        {
            // The summary is read in the interface language: a language switch tells the bar to read it again.
            _languageFollowed = true;
            text.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Summary));
        }

        Rows.Clear();
        Status = string.Empty;
        foreach (var step in stepCodes.Select(c => PipelineSteps.All.FirstOrDefault(s => s.Code == c)).OfType<PipelineStep>())
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

            // A saved model that is not offered now is shown as unavailable and counts as saved (it is kept). A voice replaced because it
            // does not speak the profile's language (said on the step) is not counted as saved, so the next prepare writes the shown voice
            // and a process never uses the old one.
            row.Saved = stale ? StaleChoice : Key(row);
            if (row.IsVoiceStep && row.Selected?.Model is not null && (row.Voice?.Value ?? string.Empty) != options.Voice)
            {
                // The shown voice is not the saved one (none was saved yet: the model's first voice is shown): written at the next prepare.
                row.Saved = StaleChoice;
            }
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
            await NoteAsync(row).ConfigureAwait(true);
        }

        OnPropertyChanged(nameof(Summary));
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
            // Never an empty reason: the caller shows Status in red.
            Status = text["picker.busy"];
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
            foreach (var row in rows.Where(r => r.Selected?.Kind == StepOptionKind.Unavailable))
            {
                // The saved model is not offered now: kept as chosen, and the process waits until it is back or the user picks another.
                row.NoteIsError = true;
                logger.LogWarning("Process not started: the saved model of the step {Step} is not available now", row.Step.Code);
                Status = text.Format("picker.not_started.unavailable", row.Title);
                return false;
            }

            foreach (var row in rows.Where(r => !r.Step.Optional && r.Selected?.Model is null))
            {
                row.Note = text["picker.choose_model"];
                row.NoteIsWarning = true;
                logger.LogWarning("Process not started: the step {Step} has no model", row.Step.Code);
                Status = text.Format("picker.not_started.model", row.Title);
                return false;
            }

            foreach (var row in rows.Where(r => r.IsVoiceStep && r.Selected?.Model is not null && r.Voices.Count == 0))
            {
                // A voice model without a voice to choose (Pocket TTS before the user added an own voice).
                row.Note = text["speech.voices.none"];
                row.NoteIsWarning = true;
                logger.LogWarning("Process not started: the narration model has no voice to choose");
                Status = text.Format("picker.not_started.voice", row.Title);
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

            foreach (var row in rows)
            {
                if (row.Selected?.Model is { } chosen && TooBig(chosen) is { } card)
                {
                    // A model of a higher memory class than this card's is never downloaded (several GB for nothing): it cannot run here.
                    row.NoteIsError = true;
                    logger.LogWarning("Process not started: {Model} needs a {Class} GB class graphics card; this one is of the {Card} GB class", chosen.Id,
                        chosen.MinVideoMemoryGb, (int)VramTiers.ForCard(card));
                    Status = text.Format("picker.not_started.memory", chosen.Name, chosen.MinVideoMemoryGb, Gigabytes(card));
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

            await SaveRowsAsync(rows).ConfigureAwait(true);
            return true;
        }
        finally
        {
            Progress = string.Empty;
            Busy = false;
        }
    }

    /// <summary>Saves the rows that changed since they were loaded or last saved (the profile's last choices).</summary>
    public Task SaveAsync() => SaveRowsAsync(Rows.ToList());

    /// <summary>Saves those of <paramref name="rows"/> that changed since they were loaded or last saved.</summary>
    private async Task SaveRowsAsync(IReadOnlyList<StepModelRowViewModel> rows)
    {
        if (_profile is not { } profile)
        {
            return;
        }

        foreach (var row in rows.Where(r => Key(r) != r.Saved))
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
            .ToList();
        var row = new StepModelRowViewModel(step, text["step." + step.Code], text["step." + step.Code + ".purpose"],
            text.Format("models.recommended", string.Join(" · ", recommended.Select(r => r.Model?.Name ?? r.Code))), text["models.effort.auto"],
            recommended.Select(r => r.Code).ToList());
        row.Options.Add(new StepOption(StepOptionKind.None, null, step.Optional ? text["step." + step.Code + ".off"] : text["models.none"]));
        if (step.Code == PipelineSteps.Images)
        {
            row.Options.Add(new StepOption(StepOptionKind.Samples, null, text["step.images.samples"]));
        }

        foreach (var model in catalog.With(step.Capability).Where(m => Usable(step, m))
            .OrderBy(m => m.Provider == ModelProviders.Local ? 0 : EndpointCodes.IsEndpoint(m.Provider) ? 1 : 2).ThenBy(m => m.Provider, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal))
        {
            row.Options.Add(Option(model));
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

        // Never replaced on its own (0.1.17): the saved model stays chosen, shown as unavailable with the reason, and is saved back unchanged.
        stale = false;
        var known = catalog.All.FirstOrDefault(m => m.Provider == choice!.Provider && m.Id == choice.Model);
        var reason = text[EndpointCodes.IsEndpoint(choice!.Provider) ? "models.unavailable.server"
            : known is not null ? "models.unavailable.language" : "models.unavailable.catalog"];
        var unavailable = new StepOption(StepOptionKind.Unavailable, null, text.Format("models.unavailable.label", known?.Name ?? choice.Model), Clone(choice));
        row.Options.Insert(1, unavailable);
        row.Unavailable = reason;
        logger.LogWarning("The saved model {Provider}/{Model} of the step {Step} is not offered now; it stays chosen and is shown as unavailable", choice.Provider,
            choice.Model, row.Step.Code);
        return unavailable;
    }

    private static StepModelChoice Clone(StepModelChoice choice) => new()
    {
        Step = choice.Step,
        ProfileId = choice.ProfileId,
        Provider = choice.Provider,
        Model = choice.Model,
        KeyId = choice.KeyId,
        OptionsJson = choice.OptionsJson,
    };

    private bool Usable(PipelineStep step, CatalogModel model) =>
        step.Capability != ModelCapability.Speech || speech.EngineOf(model) is { } engine && engine.Supports(_language);

    /// <summary>
    /// A model's entry: a local model with a graphics card class shows its class, green when this card's class sees it (its own class and
    /// every lower one), red with "this card cannot run it" above, yellow when the processor build runs it or the card's memory is unknown;
    /// a license with conditions adds a yellow badge, a non-commercial one a red badge.
    /// </summary>
    private StepOption Option(CatalogModel model)
    {
        var label = Label(model);
        bool ok = false, warning = false, error = false;
        if (model.Provider == ModelProviders.Local && model.MinVideoMemoryGb > 0 && model.Capabilities.HasFlag(ModelCapability.Image))
        {
            if (TooBig(model) is not null)
            {
                label += " · " + text["models.tier.too_big"];
                error = true;
            }
            else if (_imageChoice is { Backend: not ImageBackend.Cpu, Device.VideoMemoryBytes: > 0 })
            {
                ok = true;
            }
            else
            {
                warning = true;
            }
        }

        switch (model.LicenseUse)
        {
            case LicenseUse.NonCommercial:
                label += " · " + text["models.license.badge.noncommercial"];
                error = true;
                break;
            case LicenseUse.Limited:
                label += " · " + text["models.license.badge.limited"];
                warning = true;
                break;
        }

        return new StepOption(StepOptionKind.Model, model, label) { IsOk = ok && !warning && !error, IsWarning = warning && !error, IsError = error };
    }

    /// <summary>
    /// The card's memory when <paramref name="model"/> is of a higher graphics card class than the card the image build runs on (it cannot run
    /// here); null when it can, the build is the processor's, or the card's memory is unknown.
    /// </summary>
    private long? TooBig(CatalogModel model)
    {
        if (model.Provider != ModelProviders.Local || model.MinVideoMemoryGb <= 0 || !model.Capabilities.HasFlag(ModelCapability.Image)
            || _imageChoice is not { Backend: not ImageBackend.Cpu, Device.VideoMemoryBytes: { } bytes } || bytes <= 0)
        {
            return null;
        }

        return VramTiers.Sees(VramTiers.ForCard(bytes), VramTiers.ForModel(model.MinVideoMemoryGb)) ? null : bytes;
    }

    private string Gigabytes(long bytes) => Math.Round(bytes / (1024.0 * 1024 * 1024), 1).ToString("0.#", text.Culture);

    private string Label(CatalogModel model)
    {
        if (model.Provider == ModelProviders.Local)
        {
            return model.Name + " · " + text["models.local_free"] + (model.MinVideoMemoryGb > 0 ? " · " + text.Format("models.tier", model.MinVideoMemoryGb) : string.Empty);
        }

        if (EndpointCodes.IsEndpoint(model.Provider))
        {
            // A user-added local server: the catalog name already says "model · server name"; free, no key.
            return model.Name + " · " + text["models.endpoint_free"];
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
            or nameof(StepModelRowViewModel.Effort) or nameof(StepModelRowViewModel.Voice) or nameof(StepModelRowViewModel.Speed)))
        {
            return;
        }

        if (row.Selected is null || (e.PropertyName == nameof(StepModelRowViewModel.Effort) && row.Effort is null && row.HasEfforts))
        {
            // A picker whose list is being replaced pushes null for a moment: not a choice, nothing is saved or shown.
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

            if (e.PropertyName == nameof(StepModelRowViewModel.Selected) && row.Selected.Kind != StepOptionKind.Unavailable && row.Unavailable.Length > 0)
            {
                // The user picked another entry: the unavailable one leaves the list.
                row.Unavailable = string.Empty;
                if (row.Options.FirstOrDefault(o => o.Kind == StepOptionKind.Unavailable) is { } gone)
                {
                    row.Options.Remove(gone);
                }
            }

            OnPropertyChanged(nameof(Summary));
            Changed?.Invoke(this, EventArgs.Empty);

            // Saved at once (0.1.17): the next page switch, restart, key or server never brings an older choice back.
            await SaveRowsAsync([row]).ConfigureAwait(true);
            if (e.PropertyName is not (nameof(StepModelRowViewModel.Effort) or nameof(StepModelRowViewModel.Speed)))
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
        row.NoteIsError = false;
        row.NoteIsOk = false;
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

        if (selected.Kind == StepOptionKind.Unavailable)
        {
            row.Note = row.Unavailable;
            row.NoteIsError = true;
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
            if (row.Voices.Count == 0)
            {
                parts.Add(text["speech.voices.none"]);
                row.NoteIsWarning = true;
            }
        }

        if (model.LicenseUse != LicenseUse.Commercial)
        {
            parts.Add(text.Format(model.LicenseUse == LicenseUse.NonCommercial ? "models.license.note.noncommercial" : "models.license.note.limited",
                LicenseName(model.License)));
            row.NoteIsWarning = true;
        }

        if (model.MinVideoMemoryGb > 0)
        {
            // The first call looks at the graphics cards (native calls; on Linux maybe a short nvidia-smi run): off the UI thread.
            var choice = await Task.Run(backends.Choose).ConfigureAwait(true);
            parts.Add(text.Format("images.memory", model.MinVideoMemoryGb));
            MemoryNote(row, parts, model.MinVideoMemoryGb, choice);
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
        // One colour for the whole note: red wins over yellow, yellow over green.
        row.NoteIsWarning &= !row.NoteIsError;
        row.NoteIsOk &= !row.NoteIsError && !row.NoteIsWarning;
    }

    /// <summary>
    /// The memory part of a local image model's note for the build chosen now: the processor build (yellow: slow); the card's memory unknown
    /// (yellow); a model of a higher memory class than the card's (<see cref="VramTiers"/>; red: it will not run here; still selectable, the
    /// user decides); enough (green, with the card and the build).
    /// </summary>
    private void MemoryNote(StepModelRowViewModel row, List<string> parts, int minimumGb, ImageBackendChoice choice)
    {
        if (choice.Backend == ImageBackend.Cpu)
        {
            parts.Add(text["images.memory.cpu"]);
            row.NoteIsWarning = true;
            return;
        }

        if (choice.Device?.VideoMemoryBytes is not { } bytes)
        {
            parts.Add(text["images.memory.unknown"]);
            row.NoteIsWarning = true;
            return;
        }

        var gigabytes = Math.Round(bytes / (1024.0 * 1024 * 1024), 1);
        var shown = gigabytes.ToString("0.#", text.Culture);

        // One rule for every local model (0.1.17): the card's class (a "16 GB" card reporting 15.8 GB is in the 16 GB class) sees its own
        // class and every lower one; a model of a higher class is red.
        if (!VramTiers.Sees(VramTiers.ForCard(bytes), VramTiers.ForModel(minimumGb)))
        {
            parts.Add(text.Format("images.memory.not_enough", minimumGb, shown));
            row.NoteIsError = true;
            return;
        }

        parts.Add(text.Format("images.memory.ok", shown, choice.Device.Name, text["settings.image_acceleration." + BackendKey(choice.Backend)]));
        row.NoteIsOk = true;
    }

    /// <summary>The display name of a license code.</summary>
    private string LicenseName(string code) => text["license." + code];

    /// <summary>The language key part of a build (<c>cuda</c>, <c>vulkan</c>, <c>cpu</c>).</summary>
    internal static string BackendKey(ImageBackend backend) => backend switch
    {
        ImageBackend.Cuda => "cuda",
        ImageBackend.Vulkan => "vulkan",
        _ => "cpu",
    };

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

        var key = model.Provider + "/" + model.Id;
        if (model.LicenseUse != LicenseUse.Commercial && !settings.AcceptedModelLicenses.Contains(key))
        {
            // Asked once per model, before its first download (0.1.17: non-commercial and conditional licenses are offered with a clear warning).
            var message = text.Format(model.LicenseUse == LicenseUse.NonCommercial ? "models.license.message.noncommercial" : "models.license.message.limited",
                model.Name, LicenseName(model.License), text["license." + model.License + ".terms"], model.LicenseUrl);
            var license = await confirm.AskAsync(text.Format("models.license.title", model.Name), message, text["engines.license.accept"],
                danger: model.LicenseUse == LicenseUse.NonCommercial).ConfigureAwait(true);
            if (!license.Confirmed)
            {
                logger.LogInformation("The license of {Model} ({License}) was declined", key, model.License);
                return false;
            }

            settings.AcceptedModelLicenses.Add(key);
            await session.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);
            logger.LogInformation("Setting changed: the license of {Model} ({License}) was accepted", key, model.License);
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
            case StepOptionKind.Unavailable when row.Selected.Kept is { } kept:
                // Kept exactly as it was saved (model, effort, voice…): never rewritten from what the row shows now.
                return Clone(kept);
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

    /// <summary>
    /// A row's parts of <see cref="Summary"/>: "{short label}: {model}{ (effort)}", "off" for an optional step without a model, then the
    /// engine of the code step as a part of its own; the narration step names its voice after the model.
    /// </summary>
    private IEnumerable<string> SummaryParts(StepModelRowViewModel row)
    {
        var label = text["steps.short." + row.Step.Code];
        var model = row.Selected switch
        {
            { Kind: StepOptionKind.Samples } => text["step.images.samples"],
            { Kind: StepOptionKind.Unavailable } option => option.Label,
            { Model: { } m } => m.Name + (row.HasEfforts && row.Effort is { Value.Length: > 0 } effort ? " (" + effort.Label + ")" : string.Empty),
            _ => text[row.Step.Optional ? "steps.off" : "steps.none"],
        };
        if (row.ShowVoice && row.Voice is { } voice)
        {
            model += " " + voice.Label;
        }

        yield return label + ": " + model;
        if (row.IsEngineStep && _engines.FirstOrDefault(e => e.Kind == row.Engine?.Kind) is { } engine)
        {
            yield return text["steps.short.engine"] + ": " + engine.Name;
        }
    }

    /// <summary>What a row will be saved as, for the log (no personal data: model codes, effort, engine).</summary>
    private static string Describe(StepModelRowViewModel row)
    {
        var model = row.Selected switch
        {
            { Kind: StepOptionKind.Samples } => "the sample images",
            { Kind: StepOptionKind.Unavailable, Kept: { } kept } => kept.Provider + "/" + kept.Model + " (kept, not available now)",
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
        row.Selected?.Model is { } m ? m.Provider + "/" + m.Id : row.Selected?.Kept is { } kept ? kept.Provider + "/" + kept.Model : string.Empty, row.Effort?.Value ?? string.Empty,
        row.IsEngineStep ? row.Engine?.Kind.ToString() ?? string.Empty : string.Empty,
        row.IsVoiceStep ? row.Voice?.Value ?? string.Empty : string.Empty,
        row.IsVoiceStep ? (row.Speed ?? 1).ToString("0.00", CultureInfo.InvariantCulture) : string.Empty);
}
