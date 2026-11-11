using CommunityToolkit.Mvvm.ComponentModel;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// One step of a running video in its stage list: "✓ Narration  8/8 scenes  0:41  0.012 $", the code step with its call line ("Claude
/// Sonnet 5.5 is writing (~12,400 tokens)"), the render with the engine's line. <see cref="ProductionProgressViewModel"/> fills it.
/// </summary>
public sealed partial class StageRowViewModel(string code, string title) : ObservableObject
{
    /// <summary>The step (<see cref="Core.Videos.VideoSteps"/>).</summary>
    public string Code { get; } = code;

    /// <summary>The step's name on the screen.</summary>
    public string Title { get; } = title;

    /// <summary>Waiting, running, done, skipped or failed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph), nameof(IsDone), nameof(IsRunning), nameof(IsQuiet), nameof(IsFailed))]
    public partial StageState State { get; set; }

    /// <summary>✓ ● ○ – ✕.</summary>
    public string Glyph => State switch
    {
        StageState.Done => "✓",
        StageState.Running => "●",
        StageState.Skipped => "–",
        StageState.Failed => "✕",
        _ => "○",
    };

    /// <summary>Done (green mark).</summary>
    public bool IsDone => State == StageState.Done;

    /// <summary>Running (accent).</summary>
    public bool IsRunning => State == StageState.Running;

    /// <summary>Waiting or skipped (dimmed).</summary>
    public bool IsQuiet => State is StageState.Waiting or StageState.Skipped;

    /// <summary>Failed (red).</summary>
    public bool IsFailed => State == StageState.Failed;

    /// <summary>"8/8 scenes", "3/8", or "skipped".</summary>
    [ObservableProperty]
    public partial string CountText { get; set; } = string.Empty;

    /// <summary>How long the step ran ("0:41"); empty before it starts.</summary>
    [ObservableProperty]
    public partial string ElapsedText { get; set; } = string.Empty;

    /// <summary>What the step paid ("0.012 $", "0.11 $ (running)"); empty when nothing.</summary>
    [ObservableProperty]
    public partial string CostText { get; set; } = string.Empty;

    /// <summary>A line under the step: the code call's progress or the engine's render line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    public partial string Detail { get; set; } = string.Empty;

    /// <summary>Whether there is a line under the step.</summary>
    public bool HasDetail => Detail.Length > 0;

    /// <summary>When the step started (<see cref="System.Diagnostics.Stopwatch"/> timestamp; 0 = not yet).</summary>
    internal long StartedAt { get; set; }

    /// <summary>When the step ended (0 = not yet).</summary>
    internal long EndedAt { get; set; }

    /// <summary>What the run had paid when the step started (the step's cost is the rise since).</summary>
    internal decimal SpentAtStart { get; set; }
}
