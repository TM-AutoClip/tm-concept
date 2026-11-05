namespace TMConcept.Core.Playback;

/// <summary>Fixed formats of the in-app player.</summary>
public static class PlaybackFormat
{
    /// <summary>Sound sample rate (Hz).</summary>
    public const int SampleRate = 48_000;

    /// <summary>Sound channels.</summary>
    public const int Channels = 2;

    /// <summary>Bytes per millisecond of 16-bit sound.</summary>
    public const double BytesPerMs = SampleRate * Channels * 2 / 1000.0;

    /// <summary>Largest side of a decoded picture (the player scales it to its area; 720 keeps decoding light on a Raspberry Pi).</summary>
    public const int MaxSide = 720;

    /// <summary>Highest picture rate the player decodes.</summary>
    public const int MaxFps = 30;

    /// <summary>
    /// A frame later than this many frame lengths is dropped (the picture catches up with the sound); earlier frames wait for the clock.
    /// </summary>
    public const double LateFrames = 1.8;

    /// <summary>A frame up to this many milliseconds early is shown at once (waiting shorter is not measurable with a sleep).</summary>
    public const double EarlyToleranceMs = 4;

    /// <summary>Longest wait for the clock before a frame is shown anyway (a sound device that never starts must not freeze the picture).</summary>
    public const double MaxWaitMs = 2000;
}
