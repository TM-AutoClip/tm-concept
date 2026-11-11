using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Desktop.Concepts;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Work;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Section A of the concept page, "Generate from suggested topics" (0.1.13, the developer: "only from the suggested topics … I ticked 8
/// of 20, it must not make 20"). "Suggest topics" brings 5–100 topics within the channel's scope with the cheap model of the topics step
/// (the fine-tuning of the page bar goes along, the idea of section B never does); pressing it again keeps the ticked topics on top,
/// drops the unticked ones and adds the new ones below. "Generate selected (n)" writes exactly one concept per ticked topic, in the
/// list's order: no count box, no extra request (<see cref="TopicBrief"/>). The topics that got a concept leave the list batch by batch,
/// so after a cut or canceled run the ones still ticked are exactly the ones still to make: pressing again makes only them. More than
/// 100 ticked turns the button off with the reason. The list lives on the page only (cleared on a profile switch or a restart).
/// </summary>
public sealed partial class TopicSectionViewModel(AppSession session, TopicSuggester suggester, StepModelResolver resolver, IActivityLog activity,
    ICostCalculator costs, WorkStatus work, SpendingMeter spending, Localizer text, ILogger<TopicSectionViewModel> logger)
    : ConceptSectionBase(text, logger)
{
    private CancellationTokenSource? _suggestCancel;
    private bool _ticking;
    private int _suggestEstimate;

    /// <summary>How many topics one "Suggest topics" press asks for (5–100).</summary>
    [ObservableProperty]
    public partial decimal? TopicCount { get; set; } = TopicLimits.CountDefault;

    /// <summary>The suggested topics, the ticked ones first after a new press.</summary>
    public ObservableCollection<TopicItemViewModel> Topics { get; } = [];

    /// <summary>Whether there are topics to tick.</summary>
    public bool HasTopics => Topics.Count > 0;

    /// <summary>How many topics are ticked.</summary>
    public int CheckedCount => Topics.Count(t => t.Checked);

    /// <summary>"8/20 ticked".</summary>
    public string CheckedText => Text.Format("concepts.topics.checked", CheckedCount, Topics.Count);

    /// <summary>"Generate selected (8)".</summary>
    public override string GenerateText => Text.Format("concepts.topics.generate", CheckedCount);

    /// <summary>Topics are being asked for.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSuggest), nameof(CanGenerate))]
    public partial bool Suggesting { get; set; }

    /// <summary>Whether "Suggest topics" can be pressed (no paid call of the page runs).</summary>
    public bool CanSuggest => !Suggesting && !Running && !Runner.Busy;

    /// <summary>The price of one "Suggest topics" press next to its button (empty without a topics model).</summary>
    [ObservableProperty]
    public partial string SuggestEstimate { get; set; } = string.Empty;

    /// <summary>The estimate's details, or why there is none (its tooltip).</summary>
    [ObservableProperty]
    public partial string SuggestEstimateDetail { get; set; } = string.Empty;

    /// <summary>The steps of the running "Suggest topics" call.</summary>
    public CallStatusViewModel SuggestCall { get; } = new(text);

    /// <summary>What the last "Suggest topics" did (how many, the cost, or why it failed).</summary>
    [ObservableProperty]
    public partial string SuggestStatus { get; set; } = string.Empty;

    /// <summary>The last "Suggest topics" failed or brought nothing (its line is red).</summary>
    [ObservableProperty]
    public partial bool SuggestFailed { get; set; }

    /// <inheritdoc />
    protected override bool Paused => Suggesting;

    /// <summary>
    /// "Suggest topics": the topics step's model is made ready like any process (key, saved for the profile), then ONE call brings
    /// <see cref="TopicCount"/> topics. The cost is shown here and counted in the month's spending; any failure is shown and logged (a
    /// cut-off answer still gives the complete topics before the cut).
    /// </summary>
    [RelayCommand]
    public async Task SuggestAsync()
    {
        if (!CanSuggest || session.Profile is not { } profile)
        {
            return;
        }

        var picker = Runner.Settings.Picker;
        if (picker.Model(PipelineSteps.Topics) is null)
        {
            // The step to choose is in the folded models: they are unfolded.
            SuggestFailed = true;
            SuggestStatus = Text["topics.no_model"];
            Runner.Settings.ModelsOpen = true;
            return;
        }

        if (!Runner.TryEnter(this))
        {
            return;
        }

        SuggestFailed = false;
        SuggestStatus = string.Empty;
        Suggesting = true;
        SuggestCall.PropertyChanged += OnSuggestCall;
        SuggestCall.Preparing();
        using var cancel = new CancellationTokenSource();
        _suggestCancel = cancel;
        CatalogModel? paid = null;
        try
        {
            if (!await picker.PrepareAsync([PipelineSteps.Topics], "install.purpose.topics", cancel.Token).ConfigureAwait(true))
            {
                SuggestFailed = true;
                SuggestStatus = picker.Status;
                return;
            }

            cancel.Token.ThrowIfCancellationRequested();
            var model = await resolver.ResolveAsync(PipelineSteps.Topics, picker.Choice(PipelineSteps.Topics), CancellationToken.None).ConfigureAwait(true);
            paid = model.Model;
            SuggestCall.Begin(model.Model.Name);
            var result = await suggester.SuggestAsync(profile, model, Runner.Settings.Focus(Text.Language), Asked(), Text.Language,
                new Progress<Core.Chat.ChatProgress>(SuggestCall.Report), cancel.Token).ConfigureAwait(true);
            if (session.Profile?.Id != profile.Id)
            {
                return;
            }

            Merge(result.Topics);
            SuggestFailed = result.Topics.Count == 0;
            SuggestStatus = result.Topics.Count == 0
                ? Text.Format(result.Repeats > 0 ? "topics.all_repeats" : "topics.none", Text.Usd(result.CostUsd))
                : Text.Format(result.Cut ? "topics.done_cut" : "topics.done", result.Topics.Count, Text.Usd(result.CostUsd))
                  + (result.Repeats > 0 ? " · " + Text.Format("topics.repeats", result.Repeats) : string.Empty);
            activity.Add(result.Cut || result.Topics.Count == 0 ? ActivityLevel.Warning : ActivityLevel.Info, SuggestStatus);
        }
        catch (ModelNotReadyException ex)
        {
            logger.LogWarning("Topic suggestions not started: the {Step} step has no {Missing}", ex.Step, ex.Missing);
            SuggestFailed = true;
            SuggestStatus = Text["topics.no_model"];
        }
        catch (Core.Providers.ProviderException ex)
        {
            // What a failed call was billed is said too.
            SuggestFailed = true;
            var spent = ex.ChargedUsage is { } charged && paid is not null ? costs.Exact(paid, charged) : 0;
            SuggestStatus = Text.Format("topics.failed", ProviderErrors.Text(Text, ex.Code)) + " · " + Text.Format("cost.exact", Text.Usd(spent));
            activity.Add(ActivityLevel.Error, SuggestStatus);
        }
        catch (OperationCanceledException ex)
        {
            // What was counted for the canceled call is said (0 when it was canceled before anything was sent).
            logger.LogInformation("Topic suggestions canceled");
            SuggestStatus = Text.Format("topics.canceled", Text.Usd(ex is TopicsCanceledException counted ? counted.EstimatedUsd : 0));
            activity.Add(ActivityLevel.Warning, SuggestStatus);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Topic suggestions failed");
            SuggestFailed = true;
            SuggestStatus = Text.Format("topics.failed", ex.Message);
            activity.Add(ActivityLevel.Error, SuggestStatus);
        }
        finally
        {
            _suggestCancel = null;
            SuggestCall.PropertyChanged -= OnSuggestCall;
            SuggestCall.Stop();
            work.Clear(SuggestCall);
            Suggesting = false;
            Runner.Leave(this);
            await spending.EndAsync(SuggestCall).ConfigureAwait(true);
        }
    }

    /// <summary>Stops a running "Suggest topics" call.</summary>
    [RelayCommand]
    public void CancelSuggest() => _suggestCancel?.Cancel();

    /// <summary>Ticks every topic.</summary>
    [RelayCommand]
    public void TickAll() => Tick(true);

    /// <summary>Unticks every topic.</summary>
    [RelayCommand]
    public void TickNone() => Tick(false);

    /// <summary>Stops a running suggestion or generation of this section.</summary>
    public void CancelAll()
    {
        CancelSuggest();
        Cancel();
    }

    /// <summary>Another profile: its topics are not these (the list lives on the page only).</summary>
    public override void Clear()
    {
        base.Clear();
        foreach (var topic in Topics)
        {
            topic.PropertyChanged -= OnTicked;
        }

        Topics.Clear();
        SuggestStatus = string.Empty;
        SuggestFailed = false;
        TopicsChanged();
    }

    /// <inheritdoc />
    protected override ConceptBrief? Brief()
    {
        var ticked = Ticked();
        return ticked.Count is >= ConceptLimits.CountMin and <= ConceptLimits.CountMax ? new TopicBrief(ticked) : null;
    }

    /// <inheritdoc />
    protected override string OwnBlockReason()
    {
        var count = CheckedCount;
        return count == 0 ? Text["concepts.topics.none"]
            : count > ConceptLimits.CountMax ? Text.Format("concepts.topics.over", count)
            : string.Empty;
    }

    /// <inheritdoc />
    protected override void OnBatch(ConceptBatch batch)
    {
        // The topics that got a concept leave the list (a text listed twice is matched once per concept). The list is locked while the run
        // goes on; a ticked one is taken first, an unticked one of the same text only when no ticked one is left.
        ArgumentNullException.ThrowIfNull(batch);
        foreach (var made in batch.TopicsMade)
        {
            if ((Topics.FirstOrDefault(t => t.Checked && t.Text == made) ?? Topics.FirstOrDefault(t => t.Text == made)) is { } topic)
            {
                topic.PropertyChanged -= OnTicked;
                Topics.Remove(topic);
            }
        }

        TopicsChanged();
    }

    /// <inheritdoc />
    protected override void OnFinished(ConceptBrief brief, ConceptRunReport report)
    {
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(report);
        var left = Topics.Count(t => t.Checked && ((TopicBrief)brief).List.Contains(t.Text));
        if (left > 0 && report.Result is not null)
        {
            // A cut, canceled or failed run: the ones it did not reach stay ticked, and the line says a second press makes only them.
            Status = Text.Format("concepts.topics.left", brief.Count - left, brief.Count, left) + "\n" + report.Line;
        }
    }

    /// <inheritdoc />
    protected override void OnRefresh()
    {
        OnPropertyChanged(nameof(CanSuggest));
        _ = SafeAsync(SuggestEstimateAsync);
    }

    partial void OnTopicCountChanged(decimal? value) => _ = SafeAsync(SuggestEstimateAsync);

    /// <summary>The ticked topics stay on top in their order, the unticked ones go, the new ones come below, unticked.</summary>
    private void Merge(IReadOnlyList<TopicSuggestion> suggested)
    {
        foreach (var topic in Topics.Where(t => !t.Checked).ToList())
        {
            topic.PropertyChanged -= OnTicked;
            Topics.Remove(topic);
        }

        foreach (var topic in suggested)
        {
            var item = new TopicItemViewModel(topic.Id, topic.Text);
            item.PropertyChanged += OnTicked;
            Topics.Add(item);
        }

        TopicsChanged();
    }

    private List<string> Ticked() => Topics.Where(t => t.Checked).Select(t => t.Text).ToList();

    private void Tick(bool value)
    {
        // One refresh after the whole list, not one estimate per topic.
        _ticking = true;
        try
        {
            foreach (var topic in Topics)
            {
                topic.Checked = value;
            }
        }
        finally
        {
            _ticking = false;
        }

        TopicsChanged();
    }

    private void OnTicked(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TopicItemViewModel.Checked) && !_ticking)
        {
            TopicsChanged();
        }
    }

    private void TopicsChanged()
    {
        OnPropertyChanged(nameof(HasTopics));
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(CheckedText));
        OnPropertyChanged(nameof(GenerateText));
        if (!Running)
        {
            Refresh();
        }
    }

    private void OnSuggestCall(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CallStatusViewModel.Text) && SuggestCall.Active)
        {
            work.Show(SuggestCall, Text.Format("work.topics", SuggestCall.Text));
        }
    }

    /// <summary>How many topics a press asks for (the picker's value within 5–100).</summary>
    private int Asked() => TopicSuggester.Clamp((int)(TopicCount ?? TopicLimits.CountDefault));

    private async Task SuggestEstimateAsync()
    {
        var mine = ++_suggestEstimate;
        if (session.Profile is not { } profile || Runner.Settings.Picker.Model(PipelineSteps.Topics) is not { } model)
        {
            SuggestEstimate = string.Empty;
            SuggestEstimateDetail = Text["topics.estimate.none"];
            return;
        }

        var estimate = await suggester.EstimateAsync(profile, model, Runner.Settings.Focus(Text.Language), Asked(), Text.Language, CancellationToken.None)
            .ConfigureAwait(true);
        if (mine == _suggestEstimate)
        {
            SuggestEstimate = Text.Format("concepts.estimate.tiny", Text.Usd(estimate.MinUsd), Text.Usd(estimate.MaxUsd));
            SuggestEstimateDetail = Text.Format("topics.estimate", model.Name, Text.Usd(estimate.MinUsd), Text.Usd(estimate.MaxUsd));
        }
    }
}
