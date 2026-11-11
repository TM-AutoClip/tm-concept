using TMConcept.Core.Chat;

namespace TMConcept.Core.Videos;

/// <summary>
/// One video run's progress and what it paid, in one place (the first make and every later version): the weighted steps it does (its plan;
/// only these count toward the share), <see cref="Report"/> a share inside a step with the engine's line or the scenes done,
/// <see cref="Finish"/> a step (the next step of the plan is reported as running: the steps follow each other), <see cref="Spend"/> every
/// paid call as it is paid (also a failed one), and the code call's own progress (<see cref="Call"/>). Each change is reported at once with
/// the money spent so far, so the screen shows the cost rising during the run; the record and a failure read the same costs
/// (<see cref="Spent"/>), so nothing is counted twice. Reports may come from the engine's or the provider's thread: guarded by a lock.
/// </summary>
public sealed class VideoStepProgress
{
    private readonly IReadOnlyList<(string Step, double Weight)> _steps;
    private readonly IProgress<VideoProgress>? _progress;
    private readonly double _total;
    private readonly List<VideoCost> _spent = [];
    private readonly List<string> _plan;
    private readonly Lock _lock = new();
    private double _done;
    private string _step = string.Empty;
    private double _inside;
    private string _detail = string.Empty;
    private int _count;
    private int _of;
    private ChatProgress? _call;

    /// <summary>A run of <paramref name="steps"/> (in order, with their weights) that reports to <paramref name="progress"/>.</summary>
    public VideoStepProgress(IReadOnlyList<(string Step, double Weight)> steps, IProgress<VideoProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(steps);
        _steps = steps;
        _progress = progress;
        _total = Math.Max(steps.Sum(s => s.Weight), 1);
        _plan = steps.Select(s => s.Step).ToList();
    }

    /// <summary>The steps this run does, in order.</summary>
    public IReadOnlyList<string> Plan => _plan;

    /// <summary>Every cost line paid so far (a copy).</summary>
    public IReadOnlyList<VideoCost> Spent
    {
        get
        {
            lock (_lock)
            {
                return _spent.ToList();
            }
        }
    }

    /// <summary>What the run has paid so far.</summary>
    public decimal SpentUsd
    {
        get
        {
            lock (_lock)
            {
                return _spent.Sum(c => c.Usd);
            }
        }
    }

    /// <summary>A share 0–1 inside <paramref name="step"/>, with the engine's line or the scenes done of all (narration, images).</summary>
    public void Report(string step, double inside, string detail = "", int done = 0, int total = 0)
    {
        lock (_lock)
        {
            if (!string.Equals(step, _step, StringComparison.Ordinal))
            {
                _call = null;
            }

            _step = step;
            _inside = Math.Clamp(inside, 0, 1);
            _detail = detail;
            _count = done;
            _of = total;
        }

        Send();
    }

    /// <summary>The step is done; the next step of the plan runs now (none after the last one).</summary>
    public void Finish(string step)
    {
        lock (_lock)
        {
            _done += Weight(step);
            var at = _plan.IndexOf(step);
            _step = at >= 0 && at + 1 < _plan.Count ? _plan[at + 1] : string.Empty;
            _inside = 0;
            _detail = string.Empty;
            _count = 0;
            _of = 0;
            _call = null;
        }

        Send();
    }

    /// <summary>A paid call (or part of one): counted in the run's cost and reported at once.</summary>
    public void Spend(VideoCost cost)
    {
        ArgumentNullException.ThrowIfNull(cost);
        lock (_lock)
        {
            _spent.Add(cost);
        }

        Send();
    }

    /// <summary>The code call's progress (sent, thinking, writing with about how many tokens), reported with the running step.</summary>
    public void Call(ChatProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        lock (_lock)
        {
            _call = progress;
        }

        Send();
    }

    private double Weight(string step) => _steps.FirstOrDefault(w => string.Equals(w.Step, step, StringComparison.Ordinal)).Weight;

    private void Send()
    {
        if (_progress is null)
        {
            return;
        }

        VideoProgress report;
        lock (_lock)
        {
            var share = Math.Clamp((_done + (Weight(_step) * _inside)) / _total, 0, 1);
            report = new VideoProgress(share, _step, _detail, _spent.Sum(c => c.Usd), Plan, _count, _of, _call);
        }

        _progress.Report(report);
    }
}
