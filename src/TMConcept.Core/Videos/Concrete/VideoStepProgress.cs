namespace TMConcept.Core.Videos;

/// <summary>
/// The progress of a video run over its weighted steps (only the steps the run does count): <see cref="Report"/> a share inside a step,
/// <see cref="Finish"/> when a step is done. Used by the first make and by every later version.
/// </summary>
public sealed class VideoStepProgress(IReadOnlyList<(string Step, double Weight)> steps, IProgress<VideoProgress>? progress)
{
    private readonly double _total = Math.Max(steps.Sum(s => s.Weight), 1);
    private double _done;

    /// <summary>A share 0–1 inside <paramref name="step"/>, with an optional detail (the engine's line).</summary>
    public void Report(string step, double inside, string detail = "")
    {
        var weight = steps.FirstOrDefault(w => w.Step == step).Weight;
        progress?.Report(new VideoProgress(Math.Clamp((_done + (weight * inside)) / _total, 0, 1), step, detail));
    }

    /// <summary>The step is done.</summary>
    public void Finish(string step)
    {
        _done += steps.FirstOrDefault(w => w.Step == step).Weight;
        Report(step, 0);
    }
}
