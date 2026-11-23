namespace TMConcept.Core.Plans;

/// <summary>
/// One moment of a scene, tied to a narration sentence (<see cref="Sentence"/>, 1-based inside the scene): the camera and the actions that
/// happen while it is spoken. <see cref="Start"/> and <see cref="Duration"/> (seconds in the video) are filled by the app from the spoken
/// sentences (<see cref="PlanTiming"/>), never by the model.
/// </summary>
public sealed class PlanBeat
{
    /// <summary>The narration sentence of the scene this beat belongs to (1-based).</summary>
    public int Sentence { get; set; } = 1;

    /// <summary>Start, seconds in the video (the app's).</summary>
    public double Start { get; set; }

    /// <summary>Length, seconds (the app's).</summary>
    public double Duration { get; set; }

    /// <summary>The camera of this beat (null = it stays where it is).</summary>
    public PlanCamera? Camera { get; set; }

    /// <summary>What happens.</summary>
    public List<PlanAction> Actions { get; set; } = [];
}
