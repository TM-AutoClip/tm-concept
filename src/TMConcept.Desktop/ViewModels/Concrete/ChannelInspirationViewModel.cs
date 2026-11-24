using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Channels;
using TMConcept.Core.Chat;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;
using TMConcept.Core.References;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// "Kanaldan esinlen" (0.1.53, the developer's decision of 24 Nov 2026; rules 4, 8 and 15): a channel's address and how many sample videos (3 by
/// default, at most 5) → Start. Off the window's thread, step by step with Cancel: yt-dlp (installed after one confirmation) reads the channel's
/// public details, the samples are downloaded at a low resolution, measured and deleted at once, the numbers become a draft (target length,
/// format, language), and — unless "Without AI" is chosen or no text model is chosen on the concept page — ONE call to the concept step's model
/// writes the channel content (its estimate is shown before, the exact cost after, both also in the activity panel). Whose channel it is is
/// never checked. The preview can be edited; "Profili oluştur" makes a NEW profile (an existing one is never written over) and, when the samples
/// were measured and there is room (rule 20: at most 20), a NEW template "&lt;channel&gt; esinli" with the measured tempo (an existing template
/// never changes). Closing the window cancels a running look and deletes its temporary files (the photo with them). The channel's address and
/// texts are never written to the log.
/// </summary>
public sealed partial class ChannelInspirationViewModel : ObservableObject, IDisposable
{
    private readonly ChannelInspirationServices _services;
    private readonly Localizer _text;
    private readonly ILogger<ChannelInspirationViewModel> _logger;
    private CancellationTokenSource? _work;
    private ChannelInspiration? _inspiration;
    private ResolvedModel? _model;
    private bool _disposed;

    /// <summary>Creates.</summary>
    public ChannelInspirationViewModel(ChannelInspirationServices services, Localizer text, ILogger<ChannelInspirationViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(logger);
        _services = services;
        _text = text;
        _logger = logger;
        Languages = LanguageOptions.For(text);
    }

    /// <summary>Makes the profile ("Profili oluştur"); null = made, otherwise why not. Set by the dialog.</summary>
    public Func<ChannelProfileDraft, Task<string?>>? Create { get; set; }

    /// <summary>Raised when the window should close (the profile was made).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The last <see cref="LoadAsync"/> (tests wait for it).</summary>
    public Task Loaded { get; private set; } = Task.CompletedTask;

    /// <summary>The last look (tests wait for it).</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    /// <summary>The output languages.</summary>
    public IReadOnlyList<ChoiceOption> Languages { get; }

    /// <summary>The channel's address.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string Address { get; set; } = string.Empty;

    /// <summary>Sample videos to measure (1–5).</summary>
    [ObservableProperty]
    public partial decimal? Samples { get; set; } = ChannelLimits.SamplesDefault;

    /// <summary>Fewest samples.</summary>
    public int SamplesMin => ChannelLimits.SamplesMin;

    /// <summary>Most samples.</summary>
    public int SamplesMax => ChannelLimits.SamplesMax;

    /// <summary>Fetch the channel's photo as a suggestion.</summary>
    [ObservableProperty]
    public partial bool FetchPhoto { get; set; } = true;

    /// <summary>A text model is chosen and ready (the concept step of the selected profile).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiUnavailable))]
    public partial bool AiAvailable { get; set; }

    /// <summary>No text model: only the numbers are drafted.</summary>
    public bool AiUnavailable => !AiAvailable;

    /// <summary>The model call writes the channel content (else "Without AI": only the numbers).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WithoutAi))]
    public partial bool UseAi { get; set; }

    /// <summary>"Without AI" is chosen.</summary>
    public bool WithoutAi
    {
        get => !UseAi;
        set => UseAi = !value;
    }

    /// <summary>"With AI (Claude Haiku 5.5)".</summary>
    [ObservableProperty]
    public partial string AiLabel { get; set; } = string.Empty;

    /// <summary>The estimate of the call before it is made ("about $0.002–$0.01").</summary>
    [ObservableProperty]
    public partial string Estimate { get; set; } = string.Empty;

    /// <summary>A look runs (Cancel shows).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Idle))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(CreateProfileCommand))]
    public partial bool Busy { get; set; }

    /// <summary>No look runs.</summary>
    public bool Idle => !Busy;

    /// <summary>The share of the current step (0–100).</summary>
    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    /// <summary>The steps done and the one running, one line each.</summary>
    public ObservableCollection<string> Steps { get; } = [];

    /// <summary>What happened last (an error, a cancel, the cost).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>A status line shows.</summary>
    public bool HasStatus => Status.Length > 0;

    /// <summary>A draft is ready (the preview shows).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))]
    public partial bool HasResult { get; set; }

    /// <summary>The new profile's channel name.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Its output language (null: not sure, the user chooses).</summary>
    [ObservableProperty]
    public partial ChoiceOption? Language { get; set; }

    /// <summary>Vertical short videos.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Horizontal))]
    public partial bool Vertical { get; set; } = true;

    /// <summary>Horizontal videos.</summary>
    public bool Horizontal
    {
        get => !Vertical;
        set => Vertical = !value;
    }

    /// <summary>Target length (seconds).</summary>
    [ObservableProperty]
    public partial decimal? TargetSeconds { get; set; } = 75;

    /// <summary>The channel content.</summary>
    [ObservableProperty]
    public partial string ConceptText { get; set; } = string.Empty;

    /// <summary>The fetched channel photo (a temporary file), or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    public partial string? PhotoPath { get; set; }

    /// <summary>A channel photo was fetched.</summary>
    public bool HasPhoto => PhotoPath is not null;

    /// <summary>Keep the fetched photo as the profile's photo.</summary>
    [ObservableProperty]
    public partial bool UsePhoto { get; set; } = true;

    /// <summary>What the draft is not sure about and what was measured, one line each.</summary>
    public ObservableCollection<string> Notes { get; } = [];

    /// <summary>The template draft can be made (samples were measured and there is room).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTemplate))]
    public partial bool TemplatePossible { get; set; }

    /// <summary>The template draft shows.</summary>
    public bool HasTemplate => TemplatePossible;

    /// <summary>Also make the template draft.</summary>
    [ObservableProperty]
    public partial bool MakeTemplate { get; set; } = true;

    /// <summary>The template draft's name.</summary>
    [ObservableProperty]
    public partial string TemplateName { get; set; } = string.Empty;

    /// <summary>"Tempo: Tight · 4.2 cuts in 10 s · camera on 45 % of shots".</summary>
    [ObservableProperty]
    public partial string TemplateSummary { get; set; } = string.Empty;

    /// <summary>The yellow line when no template is drafted (the limit is full, or no sample was measured).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTemplateWarning))]
    public partial string TemplateWarning { get; set; } = string.Empty;

    /// <summary>A template warning shows.</summary>
    public bool HasTemplateWarning => TemplateWarning.Length > 0;

    /// <summary>Finds the text model and its estimate (off the window's thread; a failure only means "without AI").</summary>
    public Task LoadAsync() => Loaded = LoadCoreAsync();

    /// <summary>Starts the look (and the model call when chosen).</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    public Task StartAsync() => Running = RunAsync();

    /// <summary>Stops the running look (a download is stopped too; its files are deleted).</summary>
    [RelayCommand]
    public void Cancel() => _work?.Cancel();

    /// <summary>Makes the new profile (and the template draft when chosen and there is room).</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    public async Task CreateProfileAsync()
    {
        if (Create is null || _inspiration is not { } inspiration)
        {
            return;
        }

        var problem = Validate();
        if (problem is not null)
        {
            Status = problem;
            return;
        }

        var draft = new ChannelProfileDraft(Name.Trim(), Language!.Value, Vertical, (int)TargetSeconds!.Value, ConceptText.Trim(), UsePhoto ? PhotoPath : null);
        Busy = true;
        try
        {
            var failure = await Create(draft).ConfigureAwait(true);
            if (failure is not null)
            {
                Status = failure;
                return;
            }

            var made = _text.Format("inspire.created", draft.Name);
            if (TemplatePossible && MakeTemplate && inspiration.Tempo is { } tempo)
            {
                made += " " + await TemplateAsync(tempo).ConfigureAwait(true);
            }

            _services.Activity.Add(ActivityLevel.Info, made, _text.Format("inspire.created", ActivityPlaceholders.Name));
            Status = made;
        }
        finally
        {
            Busy = false;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Only cancelled here: the running look disposes its own token source in its finally.
        _work?.Cancel();
        DiscardDraft();
    }

    private bool CanStart() => !Busy && Address.Trim().Length > 0;

    private bool CanCreate() => !Busy && HasResult;

    private async Task LoadCoreAsync()
    {
        try
        {
            _model = await Task.Run(() => _services.ModelAsync(CancellationToken.None)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "The text model of Kanaldan esinlen could not be found; only the numbers are drafted");
            _model = null;
        }

        AiAvailable = _model is not null;
        UseAi = AiAvailable;
        if (_model is not { } model)
        {
            AiLabel = _text["inspire.ai.with"];
            Estimate = _text["inspire.ai.none"];
            return;
        }

        AiLabel = _text.Format("inspire.ai.with_model", model.Model.Name);
        var estimate = _services.Writer.Estimate(model.Model, _text.Language);
        Estimate = _text.Format("inspire.ai.estimate", _text.Usd(estimate.MinUsd), _text.Usd(estimate.MaxUsd));
    }

    private async Task RunAsync()
    {
        var address = Address.Trim();
        if (ReferenceMeasurer.Address(address) is null)
        {
            Status = _text["inspire.error.address"];
            return;
        }

        DiscardDraft();
        HasResult = false;
        Steps.Clear();
        Notes.Clear();
        Status = string.Empty;
        _work?.Dispose();
        var work = _work = new CancellationTokenSource();
        Busy = true;
        ProgressValue = 0;
        try
        {
            Step(_text["inspire.stage.prepare"]);
            if (!await _services.EnsureAsync().ConfigureAwait(true))
            {
                Status = _text["inspire.not_installed"];
                return;
            }

            var samples = ChannelSamplePicker.Clamp((int)(Samples ?? ChannelLimits.SamplesDefault));
            var progress = new Progress<ChannelProgress>(Show);
            var inspiration = await Task.Run(() => _services.Inspirer.InspectAsync(address, samples, FetchPhoto, progress, work.Token), work.Token).ConfigureAwait(true);
            if (_disposed)
            {
                _services.Inspirer.Discard(inspiration);
                return;
            }

            _inspiration = inspiration;
            await FillAsync(inspiration).ConfigureAwait(true);
            if (UseAi && _model is { } model)
            {
                await WriteAsync(inspiration, model, work.Token).ConfigureAwait(true);
            }

            Step(_text["inspire.stage.ready"]);
            HasResult = true;
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            Status = _text["inspire.cancelled"];
            Step(Status);
            DiscardDraft();
        }
        catch (ChannelInspireException ex)
        {
            Fail(_text["inspire.error." + ex.Code], ex.Detail);
            DiscardDraft();
        }
        catch (Exception ex) when (ex is ReferenceMeasureException or Core.Tools.ToolInstallException or IOException or UnauthorizedAccessException
            or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            _logger.LogWarning("Kanaldan esinlen stopped ({Error})", ex.GetType().Name);
            Fail(_text.Format("inspire.error.other", ex.Message), string.Empty);
            DiscardDraft();
        }
        finally
        {
            if (ReferenceEquals(_work, work))
            {
                _work = null;
            }

            work.Dispose();
            Busy = false;
        }
    }

    /// <summary>Fills the preview from the numbers (the channel content is a plain draft until the model writes one).</summary>
    private async Task FillAsync(ChannelInspiration inspiration)
    {
        var facts = inspiration.Facts;
        var listing = inspiration.Listing;
        var channel = listing.Name.Length > 0 ? listing.Name : _text["inspire.unnamed"];
        Name = Cut(_text.Format("inspire.profile.name", channel), ProfileLimits.NameMax);
        Language = facts.Language is { } code ? Languages.FirstOrDefault(l => l.Value == code) : null;
        Vertical = facts.Format != VideoFormat.Horizontal;
        TargetSeconds = facts.TargetSeconds ?? 75;
        ConceptText = NumbersText(inspiration);
        PhotoPath = inspiration.PhotoFile;
        UsePhoto = inspiration.PhotoFile is not null;
        Notes.Add(_text.Format("inspire.note.listed", listing.Videos.Count));
        Notes.Add(inspiration.Failed > 0
            ? _text.Format("inspire.note.measured_failed", inspiration.Measured, inspiration.Measured + inspiration.Failed, inspiration.Failed)
            : _text.Format("inspire.note.measured", inspiration.Measured));
        if (facts.Language is null)
        {
            Notes.Add(_text["inspire.note.language"]);
        }

        if (facts.Format is null)
        {
            Notes.Add(_text["inspire.note.format"]);
        }

        if (facts.TargetSeconds is null)
        {
            Notes.Add(_text["inspire.note.seconds"]);
        }

        if (FetchPhoto && inspiration.PhotoFile is null)
        {
            Notes.Add(_text["inspire.note.no_photo"]);
        }

        TemplatePossible = false;
        TemplateWarning = string.Empty;
        TemplateName = Cut(_text.Format("inspire.template.name", channel), Core.Styles.VideoStyleLimits.NameMax);
        if (inspiration.Tempo is not { } tempo)
        {
            TemplateWarning = _text["inspire.template.no_samples"];
            return;
        }

        var suggestion = ReferenceTempoMapper.Suggest(tempo);
        TemplateSummary = _text.Format("inspire.template.summary", _text["style.tempo." + suggestion.Tempo.ToString().ToLowerInvariant()],
            tempo.CutsPer10Seconds.ToString("0.#", _text.Culture), Math.Round(tempo.CameraShare * 100).ToString("0", _text.Culture));
        bool room;
        try
        {
            room = await _services.Templates.HasRoomAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "The templates could not be counted; the template draft is not offered");
            room = false;
        }

        if (!room)
        {
            TemplateWarning = _text.Format("inspire.template.limit", Core.Styles.VideoStyleLimits.Max);
            return;
        }

        TemplatePossible = true;
        MakeTemplate = true;
    }

    private async Task WriteAsync(ChannelInspiration inspiration, ResolvedModel model, CancellationToken token)
    {
        Step(_text["inspire.stage.writing"]);
        var calls = new Progress<ChatProgress>(_ => ProgressValue = Math.Min(95, ProgressValue + 5));
        try
        {
            var result = await Task.Run(() => _services.Writer.WriteAsync(inspiration, model, _text.Language, calls, token), token).ConfigureAwait(true);
            ConceptText = result.ConceptText;
            var cost = _text.Format("inspire.ai.cost", model.Model.Name, _text.Usd(result.CostUsd));
            Notes.Add(cost);
            _services.Activity.Add(ActivityLevel.Info, cost);
        }
        catch (ProviderException ex)
        {
            // The numbers are kept; the user can write the content or try again.
            var message = _text.Format("inspire.ai.failed", ex.Code);
            Notes.Add(message);
            _services.Activity.Add(ActivityLevel.Error, message);
        }
        catch (ChannelInspireException ex) when (ex.Code == "answer")
        {
            var message = _text["inspire.error.answer"];
            Notes.Add(message);
            _services.Activity.Add(ActivityLevel.Warning, message);
        }
    }

    private async Task<string> TemplateAsync(ReferenceTempoProfile tempo)
    {
        var name = TemplateName.Trim().Length > 0 ? TemplateName.Trim() : Cut(_text.Format("inspire.template.name", Name.Trim()), Core.Styles.VideoStyleLimits.NameMax);
        try
        {
            var outcome = await _services.Templates.CreateAsync(ChannelTemplateCreator.Draft(name, tempo, _services.Clock.GetUtcNow()), CancellationToken.None)
                .ConfigureAwait(true);
            if (outcome == ChannelTemplateOutcome.LimitReached)
            {
                var full = _text.Format("inspire.template.limit", Core.Styles.VideoStyleLimits.Max);
                _services.Activity.Add(ActivityLevel.Warning, full);
                return full;
            }

            return _text.Format("inspire.template.created", name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The profile is made; only the template is missing.
            _logger.LogError(ex, "The channel's template draft could not be added");
            var failed = _text.Format("inspire.template.failed", ex.Message);
            _services.Activity.Add(ActivityLevel.Error, failed);
            return failed;
        }
    }

    private string NumbersText(ChannelInspiration inspiration)
    {
        var facts = inspiration.Facts;
        var c = _text.Culture;
        var lines = new List<string> { _text["inspire.draft.intro"] };
        if (facts.Format is { } format)
        {
            lines.Add(_text[format == VideoFormat.Vertical ? "inspire.draft.vertical" : "inspire.draft.horizontal"]);
        }

        if (facts.TargetSeconds is { } seconds)
        {
            lines.Add(_text.Format("inspire.draft.seconds", seconds.ToString(c)));
        }

        if (inspiration.Tempo is { } tempo)
        {
            lines.Add(_text.Format("inspire.draft.tempo", tempo.CutsPer10Seconds.ToString("0.#", c), Math.Round(tempo.CameraShare * 100).ToString("0", c)));
        }

        lines.Add(_text["inspire.draft.todo"]);
        return string.Join("\n", lines);
    }

    private string? Validate()
    {
        if (Name.Trim().Length == 0 || Name.Trim().Length > ProfileLimits.NameMax)
        {
            return _text["profile.invalid.name"];
        }

        if (Language is null)
        {
            return _text["profile.invalid.language"];
        }

        if (TargetSeconds is null || TargetSeconds < ProfileLimits.SecondsMin || TargetSeconds > ProfileLimits.SecondsMax)
        {
            return _text.Format("profile.invalid.seconds", ProfileLimits.SecondsMin, ProfileLimits.SecondsMax);
        }

        return ConceptText.Trim().Length == 0 || ConceptText.Length > ProfileLimits.ConceptTextMax ? _text["profile.invalid.concept"] : null;
    }

    private void Show(ChannelProgress progress)
    {
        if (!Busy)
        {
            return;
        }

        var line = progress.Stage == ChannelStages.Sample
            ? _text.Format("inspire.stage.sample", progress.Index, progress.Count)
            : _text["inspire.stage." + progress.Stage];
        if (Steps.Count == 0 || Steps[^1] != line)
        {
            Step(line);
        }

        ProgressValue = Math.Round(Math.Clamp(progress.Fraction, 0, 1) * 100);
    }

    private void Step(string line) => Steps.Add(line);

    private void Fail(string message, string detail)
    {
        Status = detail.Length > 0 ? message + " (" + detail + ")" : message;
        Step(message);
        _services.Activity.Add(ActivityLevel.Error, _text["inspire.error.title"] + ": " + message);
    }

    private void DiscardDraft()
    {
        if (_inspiration is { } old)
        {
            _inspiration = null;
            _services.Inspirer.Discard(old);
        }

        PhotoPath = null;
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd();
}
