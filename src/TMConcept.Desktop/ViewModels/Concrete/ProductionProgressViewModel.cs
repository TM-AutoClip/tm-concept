using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Work;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The detailed progress of one running video (0.1.13, the developer: "while producing, the stages must be told in detail, and the money
/// spent"), shared by the queue item on the Concept → video page and the new version on the Produced page: one row per step of the work
/// (✓ done, ● running, ○ waiting, – skipped because the run does not do it, ✕ failed), each with how long it ran and what it paid, the scenes
/// done for the narration and the images, the code call's own line (thinking, writing with about how many tokens) and the engine's line
/// while it renders; on top the whole share, the time since the start and what the run has paid so far. The money also goes to the
/// spending pill while the run lasts (<see cref="SpendingMeter"/>), and the current step to the strip at the bottom (<see cref="WorkStatus"/>).
/// The times count on by themselves once a second.
/// </summary>
public sealed partial class ProductionProgressViewModel : ObservableObject
{
    private readonly Localizer _text;
    private readonly SpendingMeter _spending;
    private readonly WorkStatus _work;
    private readonly DispatcherTimer _timer;
    private long _started;
    private string _workKey = "work.video";
    private string _model = string.Empty;

    /// <summary>Creates (one per running video).</summary>
    public ProductionProgressViewModel(Localizer text, SpendingMeter spending, WorkStatus work)
    {
        _text = text;
        _spending = spending;
        _work = work;
        Call = new CallStatusViewModel(text);
        Call.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CallStatusViewModel.Text) && Call.Active && Row(VideoSteps.Code) is { } code)
            {
                code.Detail = Call.Text;
            }
        };
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick());
    }

    /// <summary>The steps, in the order of the work.</summary>
    public ObservableCollection<StageRowViewModel> Stages { get; } = [];

    /// <summary>The code call's line.</summary>
    public CallStatusViewModel Call { get; }

    /// <summary>The whole run's share, 0–100.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentText))]
    public partial double Percent { get; private set; }

    /// <summary>"62 %".</summary>
    public string PercentText => _text.Format("video.percent", Percent.ToString("0", CultureInfo.InvariantCulture));

    /// <summary>The time since the start ("4:12").</summary>
    [ObservableProperty]
    public partial string ElapsedText { get; private set; } = string.Empty;

    /// <summary>What the run has paid so far.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpentText))]
    public partial decimal SpentUsd { get; private set; }

    /// <summary>"spent 0.21 $".</summary>
    public string SpentText => _text.Format("video.spent", _text.Usd(SpentUsd));

    /// <summary>
    /// Where the run reports to: on the interface thread at once (a run whose steps finish without waiting, like a test's, would otherwise
    /// end before its reports arrive), from any other thread posted there in order.
    /// </summary>
    public IProgress<VideoProgress> Reporter => new Core.Chat.ForwardProgress<VideoProgress>(p =>
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Report(p);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Report(p));
        }
    });

    /// <summary>Whether the run is going on.</summary>
    [ObservableProperty]
    public partial bool Running { get; private set; }

    /// <summary>
    /// A run starts: every step waits (the run's plan comes with its first report). <paramref name="workKey"/> is the strip's text
    /// ("work.video", "work.version"); <paramref name="codeModel"/> names the model of the code call (empty when the run writes no code).
    /// </summary>
    public void Begin(string workKey, string codeModel)
    {
        _workKey = workKey;
        _model = codeModel ?? string.Empty;
        _started = Stopwatch.GetTimestamp();
        Call.Stop();
        Stages.Clear();
        foreach (var step in VideoSteps.All)
        {
            Stages.Add(new StageRowViewModel(step, _text["video.step." + step]));
        }

        Percent = 0;
        SpentUsd = 0;
        ElapsedText = Time(TimeSpan.Zero);
        Running = true;
        _timer.Start();
    }

    /// <summary>A report of the run, on the interface thread (<see cref="Reporter"/> brings it there).</summary>
    public void Report(VideoProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!Running)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        Percent = Math.Round(progress.Share * 100);
        SpentUsd = progress.SpentUsd;
        _spending.Live(this, progress.SpentUsd);
        var current = progress.Step.Length == 0 ? progress.Plan.Count : IndexOf(progress.Plan, progress.Step);
        foreach (var row in Stages)
        {
            var at = IndexOf(progress.Plan, row.Code);
            var state = at < 0 ? StageState.Skipped : at < current ? StageState.Done : at == current ? StageState.Running : StageState.Waiting;
            Move(row, state, now, progress.SpentUsd);
            if (state == StageState.Running)
            {
                ShowRunning(row, progress);
            }
        }

        var step = progress.Step.Length == 0 ? string.Empty : _text["video.step." + progress.Step];
        _work.Show(this, _text.Format(_workKey, Percent, step));
        Tick();
    }

    /// <summary>The run ended: made (every step it did is done) or failed / canceled (the running step is marked). The pill reads the month again.</summary>
    public async Task FinishAsync(bool ok)
    {
        var now = Stopwatch.GetTimestamp();
        foreach (var row in Stages.Where(r => r.State is StageState.Running or StageState.Waiting))
        {
            if (ok)
            {
                Move(row, StageState.Done, now, SpentUsd);
            }
            else if (row.State == StageState.Running)
            {
                row.EndedAt = now;
                row.State = StageState.Failed;
            }
        }

        Tick();
        _timer.Stop();
        Call.Stop();
        Running = false;
        _work.Clear(this);
        await _spending.EndAsync(this).ConfigureAwait(true);
    }

    private static int IndexOf(IReadOnlyList<string> plan, string step)
    {
        for (var i = 0; i < plan.Count; i++)
        {
            if (string.Equals(plan[i], step, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Time(TimeSpan elapsed) => elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    private StageRowViewModel? Row(string code) => Stages.FirstOrDefault(r => r.Code == code);

    /// <summary>A step changes state: its start and end times and the money it took are kept with it.</summary>
    private void Move(StageRowViewModel row, StageState state, long now, decimal spent)
    {
        if (row.State == state)
        {
            return;
        }

        if (state == StageState.Skipped)
        {
            row.State = state;
            row.CountText = _text["video.stage.skipped"];
            return;
        }

        if (state is StageState.Running or StageState.Done && row.StartedAt == 0)
        {
            // A step that was over before a report named it (a quick one) starts and ends at once.
            row.StartedAt = now;
            row.SpentAtStart = spent;
            if (row.Code == VideoSteps.Code && state == StageState.Running && _model.Length > 0)
            {
                Call.Begin(_model);
            }
        }

        if (state == StageState.Done)
        {
            row.EndedAt = now;
            var cost = spent - row.SpentAtStart;
            row.CostText = cost > 0 ? _text.Usd(cost) : string.Empty;
            if (row.Code == VideoSteps.Code && Call.Active)
            {
                // The code call's last line ("Answer received (21 s)") stays under the step.
                row.Detail = Call.Text;
                Call.Stop();
            }
            else if (row.Code == VideoSteps.Render)
            {
                row.Detail = string.Empty;
            }
        }

        row.State = state;
    }

    /// <summary>The running step's scenes, its line and the money it has taken so far.</summary>
    private void ShowRunning(StageRowViewModel row, VideoProgress progress)
    {
        if (progress.Total > 0)
        {
            row.CountText = row.Code == VideoSteps.Speech
                ? _text.Format("video.stage.scenes", progress.Done, progress.Total)
                : _text.Format("video.stage.count", progress.Done, progress.Total);
        }

        if (row.Code == VideoSteps.Code && progress.Call is { } call)
        {
            Call.Report(call);
        }
        else if (row.Code != VideoSteps.Code && progress.Detail.Length > 0)
        {
            row.Detail = progress.Detail;
        }

        var cost = progress.SpentUsd - row.SpentAtStart;
        row.CostText = cost > 0 ? _text.Format("video.stage.running_cost", _text.Usd(cost)) : string.Empty;
    }

    private void Tick()
    {
        if (_started == 0)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (Running)
        {
            ElapsedText = Time(Stopwatch.GetElapsedTime(_started, now));
        }

        foreach (var row in Stages.Where(r => r.StartedAt != 0))
        {
            row.ElapsedText = Time(Stopwatch.GetElapsedTime(row.StartedAt, row.EndedAt != 0 ? row.EndedAt : now));
        }
    }
}
