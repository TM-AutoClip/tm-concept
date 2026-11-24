namespace TMConcept.Core.References;

/// <summary>
/// Puts the numbers of a reference together (S1, 0.1.52): the cuts (<see cref="ReferenceCuts"/>) → cuts every 10 s and the mean shot; the
/// camera (<see cref="ReferenceCameraMeasure"/>); the colour as read; the sound → its loudness on the frame clock (<see cref="LoudnessCurve"/>),
/// the beat grid's tempo (K11, <see cref="Music.BeatTracker"/>) and, with the camera speed, the frame difference and the cut density, the
/// tension whose peaks (<see cref="TensionCurve"/>) become the emphasis moments as a share of the length. Only numbers come out. Pure.
/// </summary>
public static class ReferenceProfileBuilder
{
    /// <summary>The profile of <paramref name="input"/>; <paramref name="source"/> is the file's name or the web address.</summary>
    public static ReferenceTempoProfile Build(ReferenceMeasureInput input, string source, bool fromUrl, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        var frames = input.Frames;
        var seconds = frames.Seconds;
        var cuts = ReferenceCuts.Find(frames);
        var camera = ReferenceCameraMeasure.Measure(frames, cuts);
        double[]? loudness = null;
        var hasSound = false;
        var bpm = 0.0;
        if (input.Samples is { Length: > 0 } samples && input.SampleRate > 0)
        {
            var curve = LoudnessCurve.Rms(samples, input.SampleRate, 1 / frames.Fps);
            hasSound = curve.Any(v => v > ReferenceLimits.SilenceDb);
            if (hasSound)
            {
                loudness = curve;
                bpm = Music.BeatTracker.Analyze(samples, input.SampleRate)?.Bpm ?? 0;
            }
        }

        var tension = TensionCurve.Compute(frames.Count, frames.Fps, loudness, ReferenceCameraMeasure.Speeds(frames, cuts), frames.Change, cuts);
        var peaks = seconds > 0 ? TensionCurve.Peaks(tension, frames.Fps).Select(t => Math.Round(Math.Clamp(t / seconds, 0, 1), 3)).ToList() : [];
        var colors = input.Colors;
        return new ReferenceTempoProfile
        {
            Source = source,
            FromUrl = fromUrl,
            MeasuredAtUtc = now,
            Seconds = Math.Round(seconds, 1),
            Cuts = cuts.Count,
            CutsPer10Seconds = seconds > 0 ? Math.Round(cuts.Count / seconds * 10, 2) : 0,
            AverageShotSeconds = Math.Round(seconds / (cuts.Count + 1), 2),
            CameraShare = Math.Round(camera.Share, 2),
            CameraDirection = camera.Direction,
            Ease = camera.Ease,
            LabL = Math.Round(colors?.L ?? 0, 1),
            LabA = Math.Round(colors?.A ?? 0, 1),
            LabB = Math.Round(colors?.B ?? 0, 1),
            Brightness = Math.Round(colors?.Brightness ?? 0, 3),
            Saturation = Math.Round(colors?.Saturation ?? 0, 3),
            Palette = [.. colors?.Palette ?? []],
            HasSound = hasSound,
            Bpm = Math.Round(bpm, 1),
            Peaks = peaks,
        };
    }
}
