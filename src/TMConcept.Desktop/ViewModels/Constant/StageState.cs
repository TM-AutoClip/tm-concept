namespace TMConcept.Desktop.ViewModels;

/// <summary>A video step's state in the stage list of a running video.</summary>
public enum StageState
{
    /// <summary>Not reached yet (○).</summary>
    Waiting = 0,

    /// <summary>Running now (●).</summary>
    Running = 1,

    /// <summary>Done (✓).</summary>
    Done = 2,

    /// <summary>Not in this run's plan (–), e.g. images when they are off.</summary>
    Skipped = 3,

    /// <summary>The run failed or was canceled in this step (✕).</summary>
    Failed = 4,
}
