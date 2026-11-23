namespace TMConcept.Core.Plans;

/// <summary>
/// The plan of one scene (0.1.33, the app's drawer): what the scene stands on (<see cref="Base"/>: its generated image, a map drawn from
/// Natural Earth, a diagram of boxes and links, or a title), its named points and its beats (one per narration sentence at most, with the
/// camera and the overlay actions of that moment). The code model writes it (<see cref="PlanPromptBuilder"/>), the app cleans it
/// (<see cref="PlanReader"/>), places the image points (<see cref="ImagePointPrompt"/>), times the beats from the spoken sentences
/// (<see cref="PlanTiming"/>) and its drawer (<see cref="PlanDrawer"/>, <c>plan.js</c>) draws it. Kept in the render input, so a new
/// version is drawn the same way.
/// </summary>
public sealed class RenderPlan
{
    /// <summary>image, map, diagram or title (<see cref="PlanBases"/>).</summary>
    public string Base { get; set; } = PlanBases.Title;

    /// <summary>The map of a map scene (null otherwise).</summary>
    public PlanMap? Map { get; set; }

    /// <summary>The boxes of a diagram scene.</summary>
    public List<PlanNode> Nodes { get; set; } = [];

    /// <summary>The links between the boxes of a diagram scene.</summary>
    public List<PlanLink> Links { get; set; } = [];

    /// <summary>The named points the actions use (a place by longitude and latitude, a spot of the image by fractions).</summary>
    public List<PlanAnchor> Anchors { get; set; } = [];

    /// <summary>The text of a title scene.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The beats, in order.</summary>
    public List<PlanBeat> Beats { get; set; } = [];
}
