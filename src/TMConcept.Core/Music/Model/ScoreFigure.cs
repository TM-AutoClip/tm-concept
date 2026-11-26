namespace TMConcept.Core.Music;

/// <summary>A tune figure of composed music (0.1.55, <see cref="ProgramScoreWriter"/>): the rhythm of each of its bars, the steps between its notes and which notes rest.</summary>
/// <param name="Rhythms">Each bar's note lengths in eighth notes (<see cref="ScoreRhythms.Cells"/>).</param>
/// <param name="Steps">Each bar's moves between notes, in scale steps.</param>
/// <param name="Rests">Which notes of a bar (by position) rest instead of sounding (never the first).</param>
public sealed record ScoreFigure(IReadOnlyList<IReadOnlyList<int>> Rhythms, IReadOnlyList<IReadOnlyList<int>> Steps, IReadOnlyList<bool> Rests);
