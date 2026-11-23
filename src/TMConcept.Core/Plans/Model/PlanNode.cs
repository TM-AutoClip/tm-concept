namespace TMConcept.Core.Plans;

/// <summary>A box of a diagram scene: its id (actions point at it), its short label and its centre as fractions of the frame.</summary>
public sealed class PlanNode
{
    /// <summary>Id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Short label, in the video's language.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Centre, 0 = left, 1 = right.</summary>
    public double X { get; set; }

    /// <summary>Centre, 0 = top, 1 = bottom.</summary>
    public double Y { get; set; }
}
