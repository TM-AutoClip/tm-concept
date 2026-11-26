namespace TMConcept.Core.Music;

/// <summary>The note arithmetic of composed music (0.1.55, "Program composes"; pure): a scale's chords and notes as MIDI pitches.</summary>
public static class ScoreHarmony
{
    /// <summary>The pitch classes (0–11) of the triad on scale degree <paramref name="degree"/> of <paramref name="scale"/> in <paramref name="key"/>: root, third, fifth.</summary>
    public static IReadOnlyList<int> Chord(IReadOnlyList<int> scale, int key, int degree)
    {
        ArgumentNullException.ThrowIfNull(scale);
        return [Class(key + scale[Wrap(degree)]), Class(key + scale[Wrap(degree + 2)]), Class(key + scale[Wrap(degree + 4)])];
    }

    /// <summary>The lowest pitch of <paramref name="pitchClass"/> at or above <paramref name="low"/>.</summary>
    public static int Above(int pitchClass, int low) => low + Class(pitchClass - low);

    /// <summary>The pitch of scale step <paramref name="step"/> (0 = the key's root at <paramref name="root"/>; 7 = an octave higher; negative = below).</summary>
    public static int Step(IReadOnlyList<int> scale, int root, int step)
    {
        ArgumentNullException.ThrowIfNull(scale);
        var octave = (int)Math.Floor(step / (double)ScoreRules.ScaleLength);
        return root + (octave * ScoreRules.Octave) + scale[Wrap(step)];
    }

    /// <summary>Whether scale step <paramref name="step"/> is a note of the triad on <paramref name="degree"/>.</summary>
    public static bool InChord(int step, int degree)
    {
        var offset = Wrap(step - degree);
        return offset is 0 or 2 or 4;
    }

    /// <summary>A scale degree folded into 0–6.</summary>
    public static int Wrap(int degree) => ((degree % ScoreRules.ScaleLength) + ScoreRules.ScaleLength) % ScoreRules.ScaleLength;

    private static int Class(int pitch) => ((pitch % ScoreRules.Octave) + ScoreRules.Octave) % ScoreRules.Octave;
}
