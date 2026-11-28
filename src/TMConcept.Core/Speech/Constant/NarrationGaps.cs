namespace TMConcept.Core.Speech;

/// <summary>
/// How much silence a spoken clip may keep (0.1.24, the developer after the first real videos: "long empty gaps hurt watch time"). The scenes
/// follow the narration, so every silence a voice leaves before, inside or after its words became dead air in the video. A clip keeps only
/// short natural pauses; the user's own pause placeholders ([pause 1 s]) are added after the trim and stay as written.
/// 0.1.62 (the developer: "1-second breaths are not needed ... at commas and full stops I may stop a little, but 0.25 s is enough; and it must
/// not cut off all at once"): the margins and the longest inner pause got shorter, and every cut fades in and out over <see cref="FadeMs"/>.
/// What a listener hears between two clips is <see cref="LeadKeepMs"/> + <see cref="TailKeepMs"/> + the tempo's added silence
/// (<see cref="NarrationTempos"/> sets its totals with these margins in).
/// </summary>
public static class NarrationGaps
{
    /// <summary>Silence kept before the first word (60 ms until 0.1.61).</summary>
    public const int LeadKeepMs = 40;

    /// <summary>Silence kept after the last word: the fading end of the word, not a breath (200 ms until 0.1.61; the breath is the tempo's).</summary>
    public const int TailKeepMs = 80;

    /// <summary>
    /// Longest pause kept inside a clip (a comma, or sentences a voice spoke in one go); a longer one is shortened to this (450 ms until
    /// 0.1.61). A shorter pause, the voice's own comma breath of about 0.15–0.25 s, stays as the voice made it.
    /// </summary>
    public const int MaxPauseMs = 250;

    /// <summary>
    /// The fade at every cut of the trim (out before it, in after it) and at both ends of a clip, so a cut through a breath or room noise
    /// never clicks and no clip starts or stops abruptly (0.1.62; 10–15 ms is short enough not to touch a word).
    /// </summary>
    public const int FadeMs = 12;

    /// <summary>Length of the windows the loudness is measured in.</summary>
    public const int WindowMs = 10;

    /// <summary>A window quieter than this (RMS, full scale = 1; about −42 dBFS) is silence.</summary>
    public const double SilenceLevel = 0.008;
}
