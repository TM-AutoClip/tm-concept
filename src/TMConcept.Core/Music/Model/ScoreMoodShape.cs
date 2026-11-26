namespace TMConcept.Core.Music;

/// <summary>
/// How a mood is composed (0.1.55, "Program composes"; one per <see cref="ScoreMood"/>, <see cref="ScoreMoods"/>): major or minor,
/// the chord progression as scale degrees (0 = the key's own chord), its energy (how loud and busy the parts are) and its tempo.
/// </summary>
/// <param name="Minor">A minor key.</param>
/// <param name="Progression">The chords of four bars, as scale degrees 0–6.</param>
/// <param name="Energy">0 = calm and sparse, 1 = loud and busy.</param>
/// <param name="Bpm">The tempo when the prompt gives none.</param>
public sealed record ScoreMoodShape(bool Minor, IReadOnlyList<int> Progression, double Energy, int Bpm);
