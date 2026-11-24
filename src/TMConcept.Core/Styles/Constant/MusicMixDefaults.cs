namespace TMConcept.Core.Styles;

/// <summary>
/// The music mix (0.1.44, C3 + F4). The ducking numbers follow ReelMimic's tried values (Apache-2.0; numbers only, no code): narration
/// parts closer than <see cref="MergeGapMs"/> are one part, so the music does not rise in a breath between sentences; under narration the
/// music is <see cref="DuckDb"/> (0.25×) below its level, it goes down over <see cref="AttackMs"/> before the narration starts and comes
/// back over <see cref="ReleaseMs"/> after it ends. With the default level (−6 dB) the music under narration is about −18 dB (0.12×), the
/// bed level ReelMimic uses under a voice. The level, the ducking and the fade-out are template settings; the times are fixed.
/// </summary>
public static class MusicMixDefaults
{
    /// <summary>The music's level where nobody speaks (dB; 0 = the file as it is).</summary>
    public const double LevelDb = -6;

    /// <summary>How much lower the music is under narration (dB; −12 dB ≈ 0.25×).</summary>
    public const double DuckDb = -12;

    /// <summary>The fade-out at the end of the video (seconds).</summary>
    public const double FadeOutSeconds = 2;

    /// <summary>Narration parts closer than this are joined (a breath between sentences does not lift the music).</summary>
    public const int MergeGapMs = 600;

    /// <summary>The music goes down over this before the narration starts.</summary>
    public const int AttackMs = 150;

    /// <summary>The music comes back over this after the narration ends.</summary>
    public const int ReleaseMs = 400;

    /// <summary>Lowest level the editor allows (dB).</summary>
    public const double MinLevelDb = -40;

    /// <summary>Highest level the editor allows (dB).</summary>
    public const double MaxLevelDb = 0;

    /// <summary>Deepest ducking the editor allows (dB).</summary>
    public const double MinDuckDb = -40;

    /// <summary>Longest fade-out the editor allows (seconds).</summary>
    public const double MaxFadeOutSeconds = 10;

    /// <summary>Longest attribution text kept.</summary>
    public const int AttributionMax = 500;
}
