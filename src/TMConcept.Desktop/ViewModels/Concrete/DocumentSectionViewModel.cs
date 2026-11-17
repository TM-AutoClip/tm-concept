using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Documents;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Desktop.Concepts;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Work;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Section C of the concept page, "Belgelerden" (0.1.21): concepts that rest only on the profile's documents ("Belgelerim", the panel
/// <see cref="Library"/>). An optional subject narrows them; "how many" is the number of concepts a press writes, or the number of topics
/// "Find topics" looks for in the documents. With ticked topics the button writes exactly one concept per ticked topic (each from the
/// passages about it); without, it writes the chosen number. The ticked documents are used (none ticked = every ready one). Like the other
/// two sections it only ADDS to the page's concept list and shares the page's one paid call at a time; it never sends what the other
/// sections hold. The model may be online or local: the passages are fitted into its context either way.
/// </summary>
public sealed partial class DocumentSectionViewModel(AppSession session, DocumentTopicFinder finder, StepModelResolver resolver, IActivityLog activity,
    ICostCalculator costs, WorkStatus work, SpendingMeter spending, DocumentLibraryViewModel library, Localizer text, ILogger<DocumentSectionViewModel> logger)
    : ConceptSectionBase(text, logger)
{
    private CancellationTokenSource? _findCancel;
    private bool _attached;
    private int _findEstimate;

    /// <summary>"Belgelerim" of the open profile.</summary>
    public DocumentLibraryViewModel Library { get; } = library;

    /// <summary>The subject (optional).</summary>
    [ObservableProperty]
    public partial string Subject { get; set; } = string.Empty;

    /// <summary>How many concepts a press writes, or how many topics "Find topics" looks for (1–30).</summary>
    [ObservableProperty]
    public partial decimal? Count { get; set; } = 3;

    /// <summary>The topics found in the documents.</summary>
    public ObservableCollection<DocumentTopicItemViewModel> Topics { get; } = [];

    /// <summary>Whether there are topics.</summary>
    public bool HasTopics => Topics.Count > 0;

    /// <summary>How many topics are ticked.</summary>
    public int CheckedCount => Topics.Count(t => t.Checked);

    /// <summary>The longest subject.</summary>
    public int SubjectMax => ConceptLimits.ExtraPromptMax;

    /// <inheritdoc />
    public override string GenerateText => CheckedCount > 0
        ? Text.Format("documents.generate.topics", CheckedCount)
        : Text.Format("documents.generate", Asked());

    /// <summary>"Find topics" runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFind), nameof(CanGenerate))]
    public partial bool Finding { get; set; }

    /// <summary>Whether "Find topics" can be pressed.</summary>
    public bool CanFind => !Finding && !Running && !Runner.Busy && Library.HasReady;

    /// <summary>The steps of the running "Find topics" call.</summary>
    public CallStatusViewModel FindCall { get; } = new(text);

    /// <summary>What the last "Find topics" did.</summary>
    [ObservableProperty]
    public partial string FindStatus { get; set; } = string.Empty;

    /// <summary>The last "Find topics" failed.</summary>
    [ObservableProperty]
    public partial bool FindFailed { get; set; }

    /// <summary>The price of one "Find topics" press.</summary>
    [ObservableProperty]
    public partial string FindEstimate { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override bool Paused => Finding;

    /// <summary>Starts following the library (once).</summary>
    public void AttachLibrary()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        Library.Changed += (_, _) => Refresh();
        Library.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DocumentLibraryViewModel.Busy))
            {
                Refresh();
            }
        };
    }

    /// <summary>
    /// "Find topics": the topics step's model is made ready like any process, then ONE call looks for <see cref="Count"/> topics in the
    /// chosen documents (the passages that fit the model's context). The cost is shown and counted; every failure is shown and logged.
    /// </summary>
    [RelayCommand]
    public async Task FindTopicsAsync()
    {
        if (session.Profile is not { } profile)
        {
            Refuse(Text["profile.needed"]);
            session.AskForProfile();
            return;
        }

        if (!Library.HasReady)
        {
            Refuse(Text["documents.none_ready"]);
            return;
        }

        var picker = Runner.Settings.Picker;
        if (picker.Model(PipelineSteps.Topics) is null)
        {
            Refuse(Text["topics.no_model"]);
            Runner.Settings.ModelsOpen = true;
            return;
        }

        if (Finding || Running || !Runner.TryEnter(this))
        {
            Refuse(Text["concepts.busy_other"]);
            return;
        }

        FindFailed = false;
        FindStatus = string.Empty;
        Finding = true;
        FindCall.PropertyChanged += OnFindCall;
        FindCall.Preparing();
        using var cancel = new CancellationTokenSource();
        _findCancel = cancel;
        CatalogModel? paid = null;
        try
        {
            if (!await picker.PrepareAsync([PipelineSteps.Topics], "install.purpose.topics", cancel.Token).ConfigureAwait(true))
            {
                Refuse(picker.Status.Length > 0 ? picker.Status : Text["install.cancelled"]);
                return;
            }

            var model = await resolver.ResolveAsync(PipelineSteps.Topics, picker.Choice(PipelineSteps.Topics), CancellationToken.None).ConfigureAwait(true);
            paid = model.Model;
            FindCall.Begin(model.Model.Name);
            var result = await finder.FindAsync(profile, model, Subject, Library.Chosen, Asked(), Text.Language,
                new Progress<Core.Chat.ChatProgress>(FindCall.Report), cancel.Token).ConfigureAwait(true);
            spending.Live(FindCall, result.CostUsd);
            if (session.Profile?.Id != profile.Id)
            {
                return;
            }

            Merge(result.Topics);
            FindFailed = result.Topics.Count == 0;
            FindStatus = result.Topics.Count == 0
                ? Text.Format("documents.topics.none", Text.Usd(result.CostUsd))
                : Text.Format(result.Cut ? "topics.done_cut" : "topics.done", result.Topics.Count, Text.Usd(result.CostUsd));
            activity.Add(result.Cut || result.Topics.Count == 0 ? ActivityLevel.Warning : ActivityLevel.Info, FindStatus);
        }
        catch (ModelNotReadyException ex)
        {
            logger.LogWarning("Document topics not started: the {Step} step has no {Missing}", ex.Step, ex.Missing);
            Refuse(Text["topics.no_model"]);
        }
        catch (Core.Providers.ProviderException ex)
        {
            FindFailed = true;
            var spent = ex.ChargedUsage is { } charged && paid is not null ? costs.Exact(paid, charged) : 0;
            FindStatus = Text.Format("topics.failed", ProviderErrors.Text(Text, ex.Code)) + " · " + Text.Format("cost.exact", Text.Usd(spent));
            activity.Add(ActivityLevel.Error, FindStatus);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Document topics canceled");
            FindStatus = Text["documents.topics.canceled"];
            activity.Add(ActivityLevel.Warning, FindStatus);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Document topics failed");
            FindFailed = true;
            FindStatus = Text.Format("topics.failed", ex.Message);
            activity.Add(ActivityLevel.Error, FindStatus);
        }
        finally
        {
            _findCancel = null;
            FindCall.PropertyChanged -= OnFindCall;
            FindCall.Stop();
            work.Clear(FindCall);
            Finding = false;
            Runner.Leave(this);
            await spending.EndAsync(FindCall).ConfigureAwait(true);
        }
    }

    /// <summary>Stops a running "Find topics".</summary>
    [RelayCommand]
    public void CancelFind() => _findCancel?.Cancel();

    /// <summary>Drops the found topics.</summary>
    [RelayCommand]
    public void ClearTopics()
    {
        foreach (var topic in Topics)
        {
            topic.PropertyChanged -= OnTicked;
        }

        Topics.Clear();
        TopicsChanged();
    }

    /// <summary>Stops whatever this section runs.</summary>
    public void CancelAll()
    {
        CancelFind();
        Cancel();
        Library.Cancel();
    }

    /// <summary>Another profile: its topics and lines are not these.</summary>
    public override void Clear()
    {
        base.Clear();
        ClearTopics();
        FindStatus = string.Empty;
        FindFailed = false;
    }

    /// <inheritdoc />
    protected override ConceptBrief? Brief()
    {
        if (!Library.HasReady)
        {
            return null;
        }

        var ticked = Topics.Where(t => t.Checked).Select(t => t.Text).ToList();
        return ticked.Count > 0
            ? new DocumentBrief(Subject, ticked.Count, Library.Chosen, ticked.Take(DocumentLimits.ConceptCountMax).ToList())
            : Count is >= 1 ? new DocumentBrief(Subject, Asked(), Library.Chosen) : null;
    }

    /// <inheritdoc />
    protected override string OwnBlockReason()
    {
        if (!Library.HasReady)
        {
            return Text["documents.none_ready"];
        }

        if (Library.Busy)
        {
            return Text["documents.busy"];
        }

        return CheckedCount == 0 && Count is null or < 1 ? Text["concepts.idea.count_empty"] : string.Empty;
    }

    /// <inheritdoc />
    protected override void OnBatch(ConceptBatch batch)
    {
        // The topics that got a concept leave the list, so after a cut or canceled run the ticked ones are exactly the ones still to make.
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
    protected override void OnRefresh()
    {
        OnPropertyChanged(nameof(CanFind));
        OnPropertyChanged(nameof(GenerateText));
        _ = SafeAsync(FindEstimateAsync);
    }

    partial void OnSubjectChanged(string value) => Refresh();

    partial void OnCountChanged(decimal? value) => Refresh();

    private int Asked() => DocumentTopicFinder.Clamp((int)(Count ?? 1));

    private void Refuse(string reason)
    {
        FindFailed = true;
        FindStatus = reason;
        activity.Add(ActivityLevel.Warning, reason);
    }

    private void Merge(IReadOnlyList<DocumentTopic> found)
    {
        foreach (var topic in Topics.Where(t => !t.Checked).ToList())
        {
            topic.PropertyChanged -= OnTicked;
            Topics.Remove(topic);
        }

        var listed = Topics.Select(t => Core.Text.TurkishFold.Fold(t.Text)).ToHashSet(StringComparer.Ordinal);
        foreach (var topic in found.Where(t => listed.Add(Core.Text.TurkishFold.Fold(t.Text))))
        {
            var item = new DocumentTopicItemViewModel(topic.Text, string.Join("; ", topic.Documents));
            item.PropertyChanged += OnTicked;
            Topics.Add(item);
        }

        TopicsChanged();
    }

    private void OnTicked(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentTopicItemViewModel.Checked))
        {
            TopicsChanged();
        }
    }

    private void TopicsChanged()
    {
        OnPropertyChanged(nameof(HasTopics));
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(GenerateText));
        if (!Running)
        {
            Refresh();
        }
    }

    private void OnFindCall(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CallStatusViewModel.Text) && FindCall.Active)
        {
            work.Show(FindCall, Text.Format("work.topics", FindCall.Text));
        }
    }

    private async Task FindEstimateAsync()
    {
        var mine = ++_findEstimate;
        if (session.Profile is not { } profile || Runner.Settings.Picker.Model(PipelineSteps.Topics) is not { } model || !Library.HasReady)
        {
            FindEstimate = string.Empty;
            return;
        }

        try
        {
            var estimate = await finder.EstimateAsync(profile, model, Subject, Library.Chosen, Asked(), Text.Language, CancellationToken.None).ConfigureAwait(true);
            if (mine == _findEstimate)
            {
                FindEstimate = Text.Format("concepts.estimate.tiny", Text.Usd(estimate.MinUsd), Text.Usd(estimate.MaxUsd));
            }
        }
        catch (Core.Providers.ProviderException ex)
        {
            // No room for passages in the model's context, or no ready document: the reason instead of a price.
            if (mine == _findEstimate)
            {
                FindEstimate = ProviderErrors.Text(Text, ex.Code);
            }
        }
    }
}
