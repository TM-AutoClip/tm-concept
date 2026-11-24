namespace TMConcept.Core.Scenes;

/// <summary>
/// How a scene begins. Two separate things live here:
/// <list type="bullet">
/// <item>The placeholder of the text (kept in <c>input.json</c> as the scene's <c>transition</c>): <see cref="None"/> = no placeholder (a cut,
/// the video code decides nothing else), <see cref="Cut"/> = a hard cut asked for (the video code is told), <see cref="Fade"/> = a short fade
/// through black that ffmpeg adds at the scene's start after the render, whatever the code drew. The first scene has no transition. A
/// placeholder always wins over the plan's transition.</item>
/// <item>The drawer's transition catalog (F1, 0.1.42; kept in the scene's plan, <see cref="Plans.RenderPlan.Transition"/>): the app's own
/// list (<see cref="Catalog"/>), each with the energies it fits. Directional ones carry their direction in the name (<c>slide-left</c>: the
/// picture moves to the left). The numbers of the zoom-through (out 1 → 1.2 on power3.in, in 0.75 → 1 on expo.out, at most 10 px of blur)
/// and the rule "the way a scene leaves is the way the next one enters, one direction in a film" follow ReelMimic's transition registry and
/// motion doctrine (Apache-2.0: only the numbers and the rule, no code or template taken; <c>plan.js</c> draws them). The pixel art
/// family (F13, 0.1.51: <see cref="Dissolve"/>, <see cref="Iris"/>, <see cref="Blocks"/>) is offered only to that style
/// (<see cref="TransitionDefinition.StyleOnly"/>) and always shows the new scene over the old one, never a black frame between them.</item>
/// </list>
/// </summary>
public static class SceneTransitions
{
    /// <summary>No placeholder.</summary>
    public const string None = "";

    /// <summary>A hard cut.</summary>
    public const string Cut = "cut";

    /// <summary>A fade through black.</summary>
    public const string Fade = "fade";

    /// <summary>
    /// Length of each half of a fade: the picture goes dark over this time before the scene's start and comes back over the same time after
    /// it (about half a second in all); the timing of the scenes does not change.
    /// </summary>
    public const int FadeHalfMs = 250;

    /// <summary>The new scene fades in over the old one (no direction).</summary>
    public const string Crossfade = "crossfade";

    /// <summary>The new scene fades in out of a blur (at most 10 px) while the old one blurs away.</summary>
    public const string BlurDissolve = "blur-dissolve";

    /// <summary>The new scene slides in over the old one, which stays (directional).</summary>
    public const string Slide = "slide";

    /// <summary>The new scene pushes the old one out, both moving the same way (directional).</summary>
    public const string Push = "push";

    /// <summary>The new scene is uncovered by an edge sweeping across, like an eraser on a board (directional).</summary>
    public const string Wipe = "wipe";

    /// <summary>The camera flies through: the old scene grows 1 → 1.2 and blurs, the new one grows 0.75 → 1 out of the blur.</summary>
    public const string ZoomThrough = "zoom-through";

    /// <summary>
    /// Pixel art (F13, 0.1.51, style only): the new scene shows through an ordered (Bayer 8 × 8) dither pattern over the old one that fills
    /// in 12 steps a second, never through a colour.
    /// </summary>
    public const string Dissolve = "dissolve";

    /// <summary>Pixel art (F13, style only): the new scene opens in a growing circle from the middle, in steps of whole pixels.</summary>
    public const string Iris = "iris";

    /// <summary>
    /// Pixel art (F13, style only): the new scene comes in as rows of square blocks, each row a little after the one before, like a stair
    /// (directional).
    /// </summary>
    public const string Blocks = "blocks";

    /// <summary>The picture moves to the left (the default direction of a film).</summary>
    public const string Left = "left";

    /// <summary>The picture moves to the right.</summary>
    public const string Right = "right";

    /// <summary>The picture moves up.</summary>
    public const string Up = "up";

    /// <summary>The picture moves down.</summary>
    public const string Down = "down";

    /// <summary>The directions of a directional transition (the first is the film's default).</summary>
    public static readonly IReadOnlyList<string> Directions = [Left, Right, Up, Down];

    /// <summary>Seconds of a transition at calm energy.</summary>
    public const double CalmSeconds = 0.8;

    /// <summary>Seconds of a transition at medium energy.</summary>
    public const double MediumSeconds = 0.6;

    /// <summary>Seconds of a transition at high energy (ReelMimic's 0.4–0.5 s for its high-energy ones).</summary>
    public const double HighSeconds = 0.45;

    /// <summary>Seconds of a zoom-through, both halves (the old scene's before the start, the new one's after it).</summary>
    public const double ZoomThroughSeconds = 0.5;

    /// <summary>The app's transition catalog, in the order the plan prompt lists them.</summary>
    public static readonly IReadOnlyList<TransitionDefinition> Catalog =
    [
        new(Cut, false, [TransitionEnergy.Calm, TransitionEnergy.Medium, TransitionEnergy.High]),
        new(Crossfade, false, [TransitionEnergy.Calm, TransitionEnergy.Medium]),
        new(BlurDissolve, false, [TransitionEnergy.Calm, TransitionEnergy.Medium]),
        new(Slide, true, [TransitionEnergy.Calm, TransitionEnergy.Medium, TransitionEnergy.High]),
        new(Wipe, true, [TransitionEnergy.Medium, TransitionEnergy.High]),
        new(Push, true, [TransitionEnergy.Medium, TransitionEnergy.High]),
        new(ZoomThrough, false, [TransitionEnergy.High]),
        new(Dissolve, false, [TransitionEnergy.Calm, TransitionEnergy.Medium, TransitionEnergy.High], StyleOnly: true),
        new(Iris, false, [TransitionEnergy.Medium, TransitionEnergy.High], StyleOnly: true),
        new(Blocks, true, [TransitionEnergy.Medium, TransitionEnergy.High], StyleOnly: true),
    ];
}
