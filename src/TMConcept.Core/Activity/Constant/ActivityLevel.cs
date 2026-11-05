namespace TMConcept.Core.Activity;

/// <summary>Level of an activity log line (new values are added at the end).</summary>
public enum ActivityLevel
{
    /// <summary>Information.</summary>
    Info = 0,

    /// <summary>Warning (yellow), e.g. "narration is off — the video was produced without sound".</summary>
    Warning = 1,

    /// <summary>Error (red).</summary>
    Error = 2,
}
