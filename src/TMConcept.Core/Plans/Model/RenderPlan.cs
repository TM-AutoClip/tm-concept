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

    /// <summary>
    /// How the scene comes in (F1, 0.1.42): a full name of the drawer's catalog (<see cref="Scenes.SceneTransitions.Catalog"/>, e.g.
    /// <c>push-left</c>), chosen by the plan and settled by <see cref="PlanMotion"/>. Empty for the first scene, for a scene whose text asks for
    /// its own transition (a placeholder wins) and in a plan made before: then the scene appears at once, as it always did.
    /// </summary>
    public string Transition { get; set; } = string.Empty;

    /// <summary>Seconds of <see cref="Transition"/> (the app's, from the tempo's energy; 0 = none).</summary>
    public double TransitionSeconds { get; set; }

    /// <summary>
    /// The slow move of a still picture without camera beats (S4, 0.1.42): empty = the slight zoom it always had (natural and relaxed tempo);
    /// for a tight tempo the film's direction (<c>left</c>, <c>right</c>, <c>up</c>, <c>down</c>: a pan that way) or <c>in</c> (a push in)
    /// when the film has no directional transition.
    /// </summary>
    public string Drift { get; set; } = string.Empty;

    /// <summary>
    /// The music this scene asks for (0.1.45, C4): written by the same plan call when the template's library music is chosen by the concept,
    /// cleaned by <see cref="PlanReader"/> (words outside <see cref="Music.MusicTags"/> are dropped); null otherwise and in a plan made before.
    /// </summary>
    public Music.MusicHint? Music { get; set; }
}
