namespace TMConcept.Core.Plans;

/// <summary>
/// What the plan rules (<see cref="IPlanRule"/>) need to know besides the answer: every scene as the plan call was told (length, sentences),
/// whether each scene has a generated picture, and whether subtitles are on (then the bottom quarter is theirs).
/// </summary>
public sealed record PlanCheck(IReadOnlyList<PlanScene> Scenes, IReadOnlyList<bool> Pictures, bool Subtitles);
