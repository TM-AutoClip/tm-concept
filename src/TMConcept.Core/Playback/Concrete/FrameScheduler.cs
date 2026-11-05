namespace TMConcept.Core.Playback;

/// <summary>
/// Audio–video sync rule of the player: the sound is the clock; a frame is shown when its time has come, waits when it is early and is
/// dropped when it is more than <see cref="PlaybackFormat.LateFrames"/> frames late (so a slow computer keeps the sound and loses frames,
/// never the other way round).
/// </summary>
public static class FrameScheduler
{
    /// <summary>The decision for a frame at <paramref name="frameMs"/> when the clock says <paramref name="clockMs"/>.</summary>
    public static FrameDecision Decide(double frameMs, double clockMs, double frameLengthMs)
    {
        if (clockMs - frameMs > frameLengthMs * PlaybackFormat.LateFrames)
        {
            return FrameDecision.Drop;
        }

        return frameMs - clockMs > PlaybackFormat.EarlyToleranceMs ? FrameDecision.Wait : FrameDecision.Show;
    }

    /// <summary>Time of frame <paramref name="index"/> of a stream that starts at <paramref name="startMs"/>.</summary>
    public static double FrameTime(long startMs, long index, int fps) => startMs + (index * 1000.0 / fps);

    /// <summary>The picture rate the player decodes: the video's own, at most <see cref="PlaybackFormat.MaxFps"/>, at least 10.</summary>
    public static int Fps(double videoFps) => Math.Clamp((int)Math.Round(videoFps <= 0 ? PlaybackFormat.MaxFps : Math.Min(videoFps, PlaybackFormat.MaxFps)), 10, PlaybackFormat.MaxFps);

    /// <summary>The decoded picture size: the video's aspect inside <paramref name="maxSide"/>, even numbers (needed by the scaler).</summary>
    public static (int Width, int Height) Size(int videoWidth, int videoHeight, int maxSide = PlaybackFormat.MaxSide)
    {
        if (videoWidth <= 0 || videoHeight <= 0)
        {
            return (maxSide, maxSide * 9 / 16 / 2 * 2);
        }

        var scale = Math.Min(1.0, (double)maxSide / Math.Max(videoWidth, videoHeight));
        static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2) * 2);
        return (Even(videoWidth * scale), Even(videoHeight * scale));
    }
}
