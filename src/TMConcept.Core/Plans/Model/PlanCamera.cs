namespace TMConcept.Core.Plans;

/// <summary>Where the camera goes at a beat: a named point to centre (empty = the whole frame) and a zoom (1 = the whole frame, at most 2.2).</summary>
public sealed class PlanCamera
{
    /// <summary>The id of an anchor or a box.</summary>
    public string Focus { get; set; } = string.Empty;

    /// <summary>Zoom, 1 to 2.2.</summary>
    public double Zoom { get; set; } = 1;
}
