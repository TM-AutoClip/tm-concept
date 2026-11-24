namespace TMConcept.Core.Plans;

/// <summary>
/// What the plan rules (<see cref="IPlanRule"/>) need to know besides the answer: every scene as the plan call was told (length, sentences),
/// whether each scene has a generated picture, whether subtitles are on (then the bottom quarter is theirs), the narration tempo (its
/// targets, <see cref="TempoTargetRule"/>, 0.1.42) and the template's motion (a calm or lively motion wins over the tempo's targets).
/// <paramref name="Target"/> (S1, 0.1.52): a reference video's targets, used instead of the tempo's and checked both ways; null = the tempo's.
/// </summary>
public sealed record PlanCheck(IReadOnlyList<PlanScene> Scenes, IReadOnlyList<bool> Pictures, bool Subtitles,
    Speech.NarrationTempo Tempo = Speech.NarrationTempo.Natural, Styles.MotionIntensity Motion = Styles.MotionIntensity.Normal,
    Speech.TempoTarget? Target = null);
