using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Documents;
using TMConcept.Core.Hardware;
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
/// 0.1.24: <see cref="Pages"/> keeps a run (and "Find topics") to a page range of the ticked PDFs; <see cref="ReadAll"/> reads every
/// passage of the chosen pages first (<see cref="DocumentDigester"/>, map-reduce with the concept model) and writes the concepts from the
/// notes; <see cref="Plan"/> says before a press what that is: pages, tokens, calls, price and a rough time, as an estimate.
/// </summary>
public sealed partial class DocumentSectionViewModel(AppSession session, DocumentTopicFinder finder, StepModelResolver resolver, IActivityLog activity,
    ICostCalculator costs, WorkStatus work, SpendingMeter spending, DocumentLibraryViewModel library, Localizer text, ILogger<DocumentSectionViewModel> logger,
    TimeProvider clock, DocumentDigester digester, IVramProbe vram)
    : ConceptSectionBase(text, logger)
{
    /// <summary>
    /// How long typing in the subject pauses before the two estimates are worked out again. 0.1.23 worked them out on every key press: each
    /// one reads the chosen documents' passages and fits them into the model's context (the concept estimate also turns the subject into a
    /// vector), so typing lagged badly (the developer, 18 Nov 2026).
    /// </summary>
    public static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(400);

    private CancellationTokenSource? _typing;
    private CancellationTokenSource? _findCancel;
    private bool _attached;
    private int _findEstimate;
    private int _plan;
    private long? _card;
    private string _planKey = string.Empty;

    /// <summary>"Belgelerim" of the open profile.</summary>
    public DocumentLibraryViewModel Library { get; } = library;

    /// <summary>The subject (optional).</summary>
    [ObservableProperty]
    public partial string Subject { get; set; } = string.Empty;

    /// <summary>How many concepts a press writes, or how many topics "Find topics" looks for (1–30).</summary>
    [ObservableProperty]
    public partial decimal? Count { get; set; } = 3;

    /// <summary>The pages of the ticked PDFs a press uses, as typed ("120-180"; empty = every page).</summary>
    [ObservableProperty]
    public partial string Pages { get; set; } = string.Empty;

    /// <summary>Read every passage of the chosen pages first and write the concepts from the notes (map-reduce).</summary>
    [ObservableProperty]
    public partial bool ReadAll { get; set; }

    /// <summary>What a press reads and, with <see cref="ReadAll"/>, its calls, price and rough time (an estimate).</summary>
    [ObservableProperty]
    public partial string Plan { get; set; } = string.Empty;

    /// <summary>The plan line is a problem (an invalid page range, no room in the model's context).</summary>
    [ObservableProperty]
    public partial bool PlanIsError { get; set; }

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
        Library.UsePicker(() => Runner.Settings.Picker);
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
                new Progress<Core.Chat.ChatProgress>(FindCall.Report), cancel.Token, PageRange()).ConfigureAwait(true);
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

        // A found topic goes with its documents and search words; a run without a subject starts reading the documents at another place
        // at every press (the phase), so pressing again reaches other parts of a long document.
        var ticked = Topics.Where(t => t.Checked).Select(t => t.Topic).ToList();
        var pages = PageRange();
        return ticked.Count > 0
            ? new DocumentBrief(Subject, ticked.Count, Library.Chosen, ticked.Take(DocumentLimits.ConceptCountMax).ToList()) { Pages = pages }
            : Count is >= 1
                ? new DocumentBrief(Subject, Asked(), Library.Chosen) { Phase = System.Security.Cryptography.RandomNumberGenerator.GetInt32(1_000) / 1_000.0, Pages = pages }
                : null;
    }

    /// <summary>
    /// "Read everything" (0.1.24): before the concepts, every passage of the chosen pages is read with the concept model (made ready like for
    /// the run) and the brief then carries the notes. A failure or a cancel is shown and no concept is written; what the calls cost is counted.
    /// </summary>
    protected override async Task<ConceptBrief?> PrepareRunAsync(ConceptBrief brief, CancellationToken cancellationToken)
    {
        if (!ReadAll || brief is not DocumentBrief documents || session.Profile is not { } profile)
        {
            return brief;
        }

        var picker = Runner.Settings.Picker;
        if (!await picker.PrepareAsync([PipelineSteps.Concepts], "install.purpose.concepts", cancellationToken).ConfigureAwait(true))
        {
            ShowStatus(picker.Status.Length > 0 ? picker.Status : Text["install.cancelled"], error: true);
            return null;
        }

        try
        {
            var model = await resolver.ResolveAsync(PipelineSteps.Concepts, picker.Choice(PipelineSteps.Concepts), cancellationToken).ConfigureAwait(true);
            var digest = await digester.RunAsync(profile, model, documents.DocumentIds, documents.Pages, documents.Subject, new Progress<DocumentDigestProgress>(p =>
            {
                Progress = Text.Format("documents.digest.progress", p.Done, p.Total);
                work.Show(this, Progress);
            }), cancellationToken).ConfigureAwait(true);
            activity.Add(ActivityLevel.Info, Text.Format("documents.digest.done", digest.Notes.Count, digest.Calls, Text.Usd(digest.CostUsd)));
            return documents with { Digest = digest.Notes };
        }
        catch (Core.Providers.ProviderException ex)
        {
            ShowStatus(Text.Format("documents.digest.failed", ProviderErrors.Text(Text, ex.Code)), error: true);
            activity.Add(ActivityLevel.Error, Status);
            return null;
        }
        catch (ModelNotReadyException ex)
        {
            logger.LogWarning("Read everything not started: the {Step} step has no {Missing}", ex.Step, ex.Missing);
            ShowStatus(Text["concepts.no_model"], error: true);
            return null;
        }
        catch (OperationCanceledException)
        {
            ShowStatus(Text["documents.digest.canceled"], error: false, warning: true);
            activity.Add(ActivityLevel.Warning, Status);
            return null;
        }
        finally
        {
            work.Clear(this);
            Progress = string.Empty;
            await spending.RefreshAsync().ConfigureAwait(true);
        }
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

        if (!DocumentPageRange.TryParse(Pages, 0, out _))
        {
            return Text["documents.plan.pages_invalid"];
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
        Library.RefreshPending();
        _ = SafeAsync(FindEstimateAsync);
        _ = SafeAsync(PlanAsync);
    }

    partial void OnPagesChanged(string value) => Refresh();

    partial void OnReadAllChanged(bool value) => Refresh();

    partial void OnSubjectChanged(string value) => RefreshAfterTyping();

    /// <summary>The refresh waiting for the typing pause (a test awaits it); done when none waits.</summary>
    internal Task TypingRefresh { get; private set; } = Task.CompletedTask;

    /// <summary>How many refreshes typing caused (one per pause, not one per key).</summary>
    internal int TypingRefreshes { get; private set; }

    /// <summary>One refresh after the typing pauses for <see cref="TypingPause"/>; a newer key press starts the wait again.</summary>
    private void RefreshAfterTyping()
    {
        _typing?.Cancel();
        _typing?.Dispose();
        var typing = new CancellationTokenSource();
        _typing = typing;
        TypingRefresh = WaitThenRefreshAsync(typing.Token);
    }

    private async Task WaitThenRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TypingPause, clock, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // A newer key press: its own wait refreshes.
            return;
        }

        TypingRefreshes++;
        Refresh();
    }

    partial void OnCountChanged(decimal? value) => Refresh();

    private int Asked() => DocumentTopicFinder.Clamp((int)(Count ?? 1));

    /// <summary>The typed page range (null = every page, or one that is not a range: the button then says why).</summary>
    private DocumentPageRange? PageRange() => DocumentPageRange.TryParse(Pages, 0, out var range) ? range : null;

    /// <summary>The plan line (the newest wins): what a press reads, and with "read everything" its calls, price and rough time.</summary>
    private async Task PlanAsync()
    {
        var mine = ++_plan;
        if (session.Profile is not { } profile || !_attached || Runner.Settings.Picker.Model(PipelineSteps.Concepts) is not { } model || !Library.HasReady)
        {
            (Plan, PlanIsError, _planKey) = (string.Empty, false, string.Empty);
            return;
        }

        if (!DocumentPageRange.TryParse(Pages, 0, out var pages))
        {
            (Plan, PlanIsError, _planKey) = (Text["documents.plan.pages_invalid"], true, string.Empty);
            return;
        }

        // Typing a subject changes nothing here: the plan is read again only when what it counts changed (no database read per key).
        var key = string.Join('|', profile.Id, model.Provider, model.Id, pages, ReadAll, Library.Documents.Count, string.Join(',', Library.Chosen));
        if (key == _planKey)
        {
            return;
        }

        _planKey = key;
        try
        {
            _card ??= (await vram.MeasureAsync(null, CancellationToken.None).ConfigureAwait(true)).TotalBytes;
            var plan = await digester.PlanAsync(profile, model, Library.Chosen, pages, _card.Value, CancellationToken.None).ConfigureAwait(true);
            if (mine != _plan)
            {
                return;
            }

            Plan = ReadAll
                ? Text.Format("documents.plan.read_all", EstimateText.Tokens(Text, plan.PassageTokens), plan.Calls, plan.MapCalls, plan.ReduceCalls, model.Name,
                    EstimateText.Line(Text, plan.Estimate))
                : Text.Format("documents.plan.best", plan.Documents, plan.Pages, EstimateText.Tokens(Text, plan.PassageTokens));
            PlanIsError = false;
        }
        catch (Core.Providers.ProviderException ex)
        {
            if (mine == _plan)
            {
                (Plan, PlanIsError) = (ProviderErrors.Text(Text, ex.Code), true);
            }

            _planKey = string.Empty;
        }
    }

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
            var item = new DocumentTopicItemViewModel(topic);
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
            // Off the UI thread: it reads and fits the documents' passages.
            var (subject, chosen, asked, language, pages) = (Subject, Library.Chosen, Asked(), Text.Language, PageRange());
            var estimate = await Task.Run(() => finder.EstimateAsync(profile, model, subject, chosen, asked, language, CancellationToken.None, pages))
                .ConfigureAwait(true);
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
