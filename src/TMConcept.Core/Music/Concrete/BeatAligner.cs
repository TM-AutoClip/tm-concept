using TMConcept.Core.Speech;

namespace TMConcept.Core.Music;

/// <summary>
/// Scene changes on the music's beats (F7, 0.1.45). The narration decides how long a scene is, so the grid cannot lock it: a scene change
/// moves only by the pause between sentences, longer by up to the tempo's tolerance (<see cref="Tolerance"/>; tight is narrow) or shorter by
/// at most the clip's silence after its last word (<see cref="MusicLibraryRules.KeepSilenceSeconds"/> of it stays). The nearest beat is taken
/// when it can be reached, else the next one when it is within the tolerance, else the change stays where it was. The music starts on a bar
/// line at the start of its first scene, so its beats fall at that time plus whole beats. Pure; frames in, frames out.
/// </summary>
public static class BeatAligner
{
    /// <summary>How far a scene change may move to a beat: tight 0.12 s, natural 0.25 s, relaxed 0.35 s.</summary>
    public static double Tolerance(NarrationTempo tempo) => tempo switch
    {
        NarrationTempo.Tight => 0.12,
        NarrationTempo.Relaxed => 0.35,
        _ => 0.25,
    };

    /// <summary>
    /// The scene lengths in frames with every change after <paramref name="firstScene"/> up to <paramref name="lastScene"/> (0-based,
    /// inclusive) moved onto a beat of <paramref name="grid"/> placed from <paramref name="musicStartSeconds"/>. <paramref name="slackFrames"/>:
    /// how many frames each scene may lose (the silence after its last word).
    /// </summary>
    public static int[] Align(IReadOnlyList<int> frames, IReadOnlyList<int> slackFrames, int fps, double musicStartSeconds, BeatGrid grid,
        double toleranceSeconds, int firstScene, int lastScene)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(slackFrames);
        ArgumentNullException.ThrowIfNull(grid);
        if (fps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fps));
        }

        var result = frames.ToArray();
        var beat = grid.BeatSeconds;
        var tolerance = Math.Min(Math.Max(0, toleranceSeconds), beat / 2);
        var start = 0;
        for (var scene = 0; scene < result.Length - 1; scene++)
        {
            start += result[scene];
            var change = scene + 1;
            if (change <= firstScene || change > lastScene)
            {
                continue;
            }

            var at = start / (double)fps;
            if (at < musicStartSeconds)
            {
                continue;
            }

            var offset = (at - musicStartSeconds) / beat;
            var nearest = musicStartSeconds + (Math.Round(offset) * beat);
            var next = musicStartSeconds + (Math.Ceiling(offset - 1e-9) * beat);
            var slack = scene < slackFrames.Count ? Math.Max(0, slackFrames[scene]) : 0;
            var move = Move(nearest, at, fps, tolerance, slack) ?? Move(next, at, fps, tolerance, slack);
            if (move is not { } shift || shift == 0)
            {
                continue;
            }

            result[scene] = Math.Max(1, result[scene] + shift);
            start += shift;
        }

        return result;
    }

    /// <summary>The frames a change at <paramref name="at"/> moves to reach <paramref name="target"/>; <c>null</c> when it cannot.</summary>
    private static int? Move(double target, double at, int fps, double tolerance, int slack)
    {
        var frames = (int)Math.Round((target - at) * fps);
        if (Math.Abs(frames) > (int)Math.Floor((tolerance * fps) + 1e-9))
        {
            return null;
        }

        return frames < 0 && -frames > slack ? null : frames;
    }

    /// <summary>
    /// The transitions where the music is loud get shorter (F1, 0.1.45): a scene whose start falls on music louder than
    /// <see cref="MusicLibraryRules.LoudShare"/> of the track's median keeps <see cref="MusicLibraryRules.LoudTransitionShare"/> of its
    /// transition's seconds. <paramref name="starts"/>: each scene's start in seconds of the video; <paramref name="musicAt"/>: the track's time
    /// for a video time (null before the music). Returns the factor of each scene (1 = unchanged).
    /// </summary>
    public static double[] TransitionFactors(IReadOnlyList<double> starts, MusicEnergy energy, Func<double, double?> musicAt)
    {
        ArgumentNullException.ThrowIfNull(starts);
        ArgumentNullException.ThrowIfNull(energy);
        ArgumentNullException.ThrowIfNull(musicAt);
        var median = MusicBedPlanner.Median(energy.Rms);
        var factors = new double[starts.Count];
        for (var i = 0; i < starts.Count; i++)
        {
            factors[i] = 1;
            if (i > 0 && median > 0 && musicAt(starts[i]) is { } time && MusicBedPlanner.At(energy, time) > median * MusicLibraryRules.LoudShare)
            {
                factors[i] = MusicLibraryRules.LoudTransitionShare;
            }
        }

        return factors;
    }
}
