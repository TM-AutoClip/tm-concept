namespace TMConcept.Core.Speech;

/// <summary>
/// How much breathing room the narration gets (0.1.31, a template setting; new values are added at the end, stored as numbers). The
/// developer after the 23 Nov 2026 videos: "it speaks too fast, there is no pause between the sentences". 0.1.24 had cut every silence to
/// keep the watch time up; the tempo puts measured silences back between sentences and between scenes (<see cref="NarrationTempos"/>).
/// </summary>
public enum NarrationTempo
{
    /// <summary>Tight: short breaths (close to the narration before 0.1.31).</summary>
    Tight = 0,

    /// <summary>Natural (the default): a clear pause after every sentence and a longer one between scenes.</summary>
    Natural = 1,

    /// <summary>Relaxed: long pauses, for calm or reflective channels.</summary>
    Relaxed = 2,
}
