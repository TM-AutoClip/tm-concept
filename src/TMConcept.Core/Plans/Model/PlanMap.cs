namespace TMConcept.Core.Plans;

/// <summary>The map of a map scene: the region shown, west, south, east and north in degrees (the drawer fits it into the safe area).</summary>
public sealed class PlanMap
{
    /// <summary>West, south, east, north (degrees).</summary>
    public List<double> Region { get; set; } = [];
}
