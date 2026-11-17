using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Concepts;
using TMConcept.Core.Pipeline;
using TMConcept.Desktop.Concepts;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// One of the generate sections of the concept page (four since 0.1.22) (0.1.13: "two separate sections, each with its own button"): its own button,
/// tiny estimate, status line, progress and Cancel. What it asks for is its <see cref="Brief"/> alone, so a section never sends the other
/// one's input. Both share the page's <see cref="ConceptRunner"/>: one paid call at a time, and while the other section runs the button is
/// off with the reason (<see cref="Blocked"/>).
/// </summary>
public abstract partial class ConceptSectionBase(Localizer text, ILogger logger) : ObservableObject
{
    private ConceptRunner? _runner;
    private CancellationTokenSource? _cancel;
    private int _estimate;

    /// <summary>The page's runner (attached by the page).</summary>
    protected ConceptRunner Runner => _runner ?? throw new InvalidOperationException("The concept page attaches its runner first.");

    /// <summary>The interface texts.</summary>
    protected Localizer Text { get; } = text;

    /// <summary>Whether the page's one call is held (false before the page attached its runner).</summary>
    protected bool RunnerBusy => _runner?.Busy == true;

    /// <summary>The estimate next to the button ("≈ 0.02 $–0.05 $").</summary>
    [ObservableProperty]
    public partial string Estimate { get; set; } = string.Empty;

    /// <summary>The estimate's details (its tooltip).</summary>
    [ObservableProperty]
    public partial string EstimateDetail { get; set; } = string.Empty;

    /// <summary>The last run's line (what it made and cost, or why it stopped).</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>The status line is red.</summary>
    [ObservableProperty]
    public partial bool StatusIsError { get; set; }

    /// <summary>The status line is yellow (a cut answer, a cancel).</summary>
    [ObservableProperty]
    public partial bool StatusIsWarning { get; set; }

    /// <summary>The running call in one line.</summary>
    [ObservableProperty]
    public partial string Progress { get; set; } = string.Empty;

    /// <summary>The steps of this section's running call.</summary>
    public CallStatusViewModel Call { get; } = new(text);

    /// <summary>This section's run is going on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate), nameof(IsBlocked))]
    public partial bool Running { get; set; }

    /// <summary>Why the button is off (empty = it can be pressed).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate), nameof(IsBlocked))]
    public partial string Blocked { get; set; } = string.Empty;

    /// <summary>Whether a reason is shown.</summary>
    public bool IsBlocked => Blocked.Length > 0 && !Running;

    /// <summary>The text of the section's generate button.</summary>
    public abstract string GenerateText { get; }

    /// <summary>Whether the button can be pressed.</summary>
    public bool CanGenerate => Blocked.Length == 0 && !Running && !Paused;

    /// <summary>
    /// The section holds the page's call for something else of its own (topic suggestions): the button is off without a reason line, the
    /// section shows that call instead.
    /// </summary>
    protected virtual bool Paused => false;

    /// <summary>Uses the page's runner; the section follows who runs.</summary>
    public void Attach(ConceptRunner runner)
    {
        _runner = runner;
        runner.Changed += (_, _) => Refresh();
        runner.Settings.Changed += (_, _) => Refresh();
    }

    /// <summary>Generates what <see cref="Brief"/> asks for.</summary>
    [RelayCommand]
    public Task GenerateAsync() => CanGenerate && Brief() is { } brief ? RunAsync(brief) : Task.CompletedTask;

    /// <summary>Stops this section's run at once (the batches saved before stay).</summary>
    [RelayCommand]
    public void Cancel() => _cancel?.Cancel();

    /// <summary>Forgets the last run's lines (another profile).</summary>
    public virtual void Clear() => ClearStatus();

    /// <summary>Tells again why the button is off and what a press would cost.</summary>
    public void Refresh()
    {
        Blocked = BlockReason();
        OnPropertyChanged(nameof(CanGenerate));
        OnRefresh();
        _ = SafeAsync(EstimateAsync);
    }

    /// <summary>Something the section shows besides its button may have changed (the runner, a model, the fine-tuning).</summary>
    protected virtual void OnRefresh()
    {
    }

    /// <summary>What a press asks for now; null when there is nothing to ask for.</summary>
    protected abstract ConceptBrief? Brief();

    /// <summary>The section's own reason why a press cannot run now (empty = none).</summary>
    protected virtual string OwnBlockReason() => string.Empty;

    /// <summary>A batch of this section's run was saved.</summary>
    protected virtual void OnBatch(ConceptBatch batch)
    {
    }

    /// <summary>This section's run of <paramref name="brief"/> ended with <paramref name="report"/>.</summary>
    protected virtual void OnFinished(ConceptBrief brief, ConceptRunReport report)
    {
    }

    /// <summary>Runs <paramref name="brief"/> and shows its report.</summary>
    protected async Task RunAsync(ConceptBrief brief)
    {
        if (Running || Runner.Busy)
        {
            return;
        }

        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        Running = true;
        ClearStatus();
        ConceptBrief? ready = null;
        try
        {
            ready = await PrepareRunAsync(brief, cancel.Token).ConfigureAwait(true);
            if (ready is null)
            {
                return;
            }

            var report = await Runner.RunAsync(ready, this, Call, line => Progress = line, OnBatch, cancel.Token).ConfigureAwait(true);
            Status = report.Line;
            StatusIsError = report.Level == ActivityLevel.Error;
            StatusIsWarning = report.Level == ActivityLevel.Warning;
            OnFinished(ready, report);
        }
        finally
        {
            _cancel = null;
            if (ready is not null)
            {
                await SafeAsync(() => AfterRunAsync(ready)).ConfigureAwait(true);
            }

            Running = false;
            Refresh();
        }
    }

    /// <summary>
    /// What runs before the model call (0.1.22: research fetches and saves its pages here) and the brief that then runs; null = nothing
    /// runs (the section has shown why). <paramref name="cancellationToken"/> is the section's Cancel.
    /// </summary>
    protected virtual Task<ConceptBrief?> PrepareRunAsync(ConceptBrief brief, CancellationToken cancellationToken) => Task.FromResult<ConceptBrief?>(brief);

    /// <summary>What runs after the model call of <paramref name="ran"/>, whatever its outcome (research takes away pages the user did not keep).</summary>
    protected virtual Task AfterRunAsync(ConceptBrief ran) => Task.CompletedTask;

    /// <summary>Shows a line on the section's status (red or yellow).</summary>
    protected void ShowStatus(string line, bool error, bool warning = false)
    {
        Status = line;
        StatusIsError = error;
        StatusIsWarning = warning;
    }

    /// <summary>Runs <paramref name="work"/>; a failure is shown on the section and logged.</summary>
    protected async Task SafeAsync(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Concept section update failed");
            Status = Text.Format("error.generic", ex.Message);
            StatusIsError = true;
        }
    }

    private void ClearStatus()
    {
        Status = string.Empty;
        StatusIsError = false;
        StatusIsWarning = false;
    }

    private string BlockReason()
    {
        if (_runner is null)
        {
            return string.Empty;
        }

        if (Runner.Busy && !ReferenceEquals(Runner.Owner, this))
        {
            return Text["concepts.busy_other"];
        }

        var own = OwnBlockReason();
        if (own.Length > 0)
        {
            return own;
        }

        return Runner.Settings.Picker.Model(PipelineSteps.Concepts) is null ? Text["concepts.no_model"] : string.Empty;
    }

    /// <summary>The newest estimate wins (estimates of quick changes overlap).</summary>
    private async Task EstimateAsync()
    {
        var mine = ++_estimate;
        var line = await Runner.EstimateAsync(Brief()).ConfigureAwait(true);
        if (mine == _estimate)
        {
            Estimate = line.Tiny;
            EstimateDetail = line.Detail;
        }
    }
}
