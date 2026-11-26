namespace TMConcept.Core.Music;

/// <summary>
/// What a prompt asks of a composed piece (0.1.55, <see cref="ScorePromptReader"/>): the mood and the style (<see cref="ScoreMood"/>, <see cref="ScoreStyle"/>),
/// the scale colour of a world or medieval prompt (<see cref="ScoreScales"/>; null = plain major or minor) and the tempo.
/// </summary>
/// <param name="Mood">The mood.</param>
/// <param name="Style">The style (the instruments).</param>
/// <param name="Flavor">The scale colour (null = the mood's major or minor).</param>
/// <param name="Bpm">The tempo.</param>
public sealed record ScoreTraits(ScoreMood Mood, ScoreStyle Style, ScoreStyle? Flavor, int Bpm);
