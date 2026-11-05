namespace TMConcept.Core.Runs;

/// <summary>State of a run (new values are added at the end).</summary>
public enum RunStatus
{
    /// <summary>Running.</summary>
    Running = 0,

    /// <summary>Finished.</summary>
    Succeeded = 1,

    /// <summary>Failed.</summary>
    Failed = 2,

    /// <summary>Canceled by the user.</summary>
    Canceled = 3,
}
