namespace TMConcept.Core.Playback;

/// <summary>What to do with a decoded frame at the current clock.</summary>
public enum FrameDecision
{
    /// <summary>Show it now.</summary>
    Show = 0,

    /// <summary>It is early: wait for the clock.</summary>
    Wait = 1,

    /// <summary>It is too late: skip it.</summary>
    Drop = 2,
}
