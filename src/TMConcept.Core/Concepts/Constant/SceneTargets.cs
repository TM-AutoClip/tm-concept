namespace TMConcept.Core.Concepts;

/// <summary>
/// The scene rules of a concept (20 Nov 2026, the developer: the scenes come as JSON and the program checks them). The program computes the
/// scene count from the target length (about one scene every <see cref="SecondsPerScene"/> s) and states it in the concept prompt; the check
/// accepts a few scenes less (<see cref="ToleranceShare"/>) and refuses a scene whose narration is longer than <see cref="MaxSceneSeconds"/>.
/// </summary>
public static class SceneTargets
{
    /// <summary>Seconds of narration per scene the prompt aims at (between 12 and 15 s).</summary>
    public const double SecondsPerScene = 13.5;

    /// <summary>The longest narration of one scene, in estimated seconds (one picture cannot carry more).</summary>
    public const double MaxSceneSeconds = 25;

    /// <summary>The share of the target count that may be missing before the check refuses (at least one scene).</summary>
    public const double ToleranceShare = 0.2;
}
