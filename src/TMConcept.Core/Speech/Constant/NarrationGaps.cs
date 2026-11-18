namespace TMConcept.Core.Speech;

/// <summary>
/// How much silence a spoken clip may keep (0.1.24, the developer after the first real videos: "long empty gaps hurt watch time"). The scenes
/// follow the narration, so every silence a voice leaves before, inside or after its words became dead air in the video. A clip keeps only
/// short natural pauses; the user's own pause placeholders ([pause 1 s]) are added after the trim and stay as written.
/// </summary>
public static class NarrationGaps
{
    /// <summary>Silence kept before the first word.</summary>
    public const int LeadKeepMs = 60;

    /// <summary>Silence kept after the last word (the breath before the next scene).</summary>
    public const int TailKeepMs = 200;

    /// <summary>Longest pause kept inside a clip (between sentences); a longer one is shortened to this.</summary>
    public const int MaxPauseMs = 450;

    /// <summary>Length of the windows the loudness is measured in.</summary>
    public const int WindowMs = 10;

    /// <summary>A window quieter than this (RMS, full scale = 1; about −42 dBFS) is silence.</summary>
    public const double SilenceLevel = 0.008;
}
