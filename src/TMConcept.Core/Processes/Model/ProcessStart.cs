namespace TMConcept.Core.Processes;

/// <summary>What to run.</summary>
public sealed record ProcessStart(string Program, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    /// <summary>Extra environment variables (after the clean-up).</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// A line in the log every so often while it runs, with the time so far and its last output line (0.1.59: a local music run that goes on
    /// for many minutes is never silent in the log); null = none.
    /// </summary>
    public TimeSpan? Heartbeat { get; init; }

    /// <summary>
    /// The start of the program's progress lines (0.1.61: a local music script's "TMC-P:"): they go to the line handler only, never into the
    /// kept last lines (a line a second would push a failure's own lines out of the log); the heartbeat still names one as the last line.
    /// Null = every line is kept.
    /// </summary>
    public string? ProgressMark { get; init; }
}
