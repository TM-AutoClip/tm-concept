namespace TMConcept.Core.Music;

/// <summary>
/// The fixed facts of one composed piece (0.1.55, <see cref="ProgramScoreWriter"/>): its scale and key, its mood, how many bars it has, the
/// mood's velocity, and which bar is the intro, the last one, in which section, on which chord.
/// </summary>
/// <param name="scale">The scale (semitones above the root).</param>
/// <param name="key">The key's root (pitch class 0–11).</param>
/// <param name="shape">The mood.</param>
/// <param name="bars">How many bars.</param>
/// <param name="velocity">The mood's velocity.</param>
public sealed class ScorePlan(IReadOnlyList<int> scale, int key, ScoreMoodShape shape, int bars, int velocity)
{
    /// <summary>The scale.</summary>
    public IReadOnlyList<int> Scale => scale;

    /// <summary>The key's root.</summary>
    public int Key => key;

    /// <summary>How many bars.</summary>
    public int Bars => bars;

    /// <summary>The mood's velocity.</summary>
    public int Velocity => velocity;

    /// <summary>The mood's energy.</summary>
    public double Energy => shape.Energy;

    /// <summary>Whether <paramref name="bar"/> is one of the intro's bars (held chords and bass only).</summary>
    public bool Intro(int bar) => bar < ScoreRules.IntroBars;

    /// <summary>Whether <paramref name="bar"/> is the last one.</summary>
    public bool Last(int bar) => bar == bars - 1;

    /// <summary>The section of <paramref name="bar"/> (0 = the first one after the intro; the intro counts as 0).</summary>
    public int Section(int bar) => bar < ScoreRules.IntroBars ? 0 : (bar - ScoreRules.IntroBars) / ScoreRules.SectionBars;

    /// <summary>The scale degree of a bar's chord: the progression, and the key's own chord in the last bar.</summary>
    public int Degree(int bar) => Last(bar) ? 0 : shape.Progression[bar % shape.Progression.Count];

    /// <summary>The pitch classes of a bar's chord (root, third, fifth).</summary>
    public IReadOnlyList<int> Chord(int bar) => ScoreHarmony.Chord(scale, key, Degree(bar));
}
