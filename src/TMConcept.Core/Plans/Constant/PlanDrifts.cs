namespace TMConcept.Core.Plans;

/// <summary>
/// The slow move of a still picture (<see cref="RenderPlan.Drift"/>, S4, 0.1.42) besides the directions of
/// <see cref="Scenes.SceneTransitions.Directions"/>: <see cref="Zoom"/> = the slight zoom of every picture before 0.1.42, <see cref="In"/> = a
/// push in. The sizes live in the drawer (plan.js): a pan is drawn at 1.12 (calm 1.06) and travels 80 % of the room that leaves.
/// </summary>
public static class PlanDrifts
{
    /// <summary>The slight zoom it always had (1 to 1.08, calm 1.04).</summary>
    public const string Zoom = "";

    /// <summary>A push in (1 to 1.12, calm 1.06).</summary>
    public const string In = "in";
}
