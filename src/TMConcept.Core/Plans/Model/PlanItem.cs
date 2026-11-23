namespace TMConcept.Core.Plans;

/// <summary>One scene of the plan answer as read (<see cref="PlanReader"/>): the scene's number (1-based) and its plan.</summary>
public sealed class PlanItem
{
    /// <summary>The scene's number (1-based).</summary>
    public int Scene { get; set; }

    /// <summary>The drawer's plan (base, points, beats).</summary>
    public RenderPlan Plan { get; set; } = new();
}
