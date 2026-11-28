namespace TMConcept.Core.Music;

/// <summary>
/// How far a piece of music is (0.1.61, plan K14/K15): the stage of all (a local script's own stages, e.g. YuE 1's tokens, blocks and decoding)
/// and how much of that stage is done, in its unit. A stage whose length is not known has <see cref="Total"/> 0 (the screen shows the stage
/// without a percentage); <see cref="Unknown"/> is a piece that counts nothing (an online model, ACE-Step): an indeterminate bar with the time
/// so far. The percentage is the running stage's (the developer's K14: "stage k/n + that stage's percentage").
/// </summary>
/// <param name="Stage">The running stage, from 1 (0 = none: <see cref="Unknown"/>).</param>
/// <param name="Stages">How many stages the run has (0 = none).</param>
/// <param name="Done">How much of the stage is done.</param>
/// <param name="Total">The stage's length (0 = not known).</param>
/// <param name="Unit">What the stage counts.</param>
public sealed record MusicProgress(int Stage, int Stages, long Done, long Total, MusicProgressUnit Unit)
{
    /// <summary>A piece that counts nothing (yet): an online model, ACE-Step, a local model before its script's first line.</summary>
    public static MusicProgress Unknown { get; } = new(0, 0, 0, 0, MusicProgressUnit.None);

    /// <summary>The stage has a length (a percentage can be shown).</summary>
    public bool Counted => Total > 0;

    /// <summary>The run names its stages.</summary>
    public bool Staged => Stage > 0 && Stages > 0;

    /// <summary>The running stage's percentage, 0–100 (0 without a length).</summary>
    public int Percent => Counted ? (int)Math.Clamp(Done * 100 / Total, 0, 100) : 0;

    /// <summary>
    /// Whether this report goes to the log after <paramref name="logged"/>, the last one that did: a new stage, or the stage's percentage past
    /// the next whole <see cref="MusicGenerationRules.ProgressLogPercent"/> (the scripts report up to once a second; the log takes every 10 %).
    /// </summary>
    public bool LogsAfter(MusicProgress? logged) =>
        logged is null || logged.Stage != Stage || logged.Stages != Stages || logged.Unit != Unit
        || (Counted && Percent / MusicGenerationRules.ProgressLogPercent > logged.Percent / MusicGenerationRules.ProgressLogPercent);
}
