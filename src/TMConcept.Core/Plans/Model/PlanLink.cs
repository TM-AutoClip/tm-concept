namespace TMConcept.Core.Plans;

/// <summary>A line between two boxes of a diagram scene (drawn from the scene's start).</summary>
public sealed class PlanLink
{
    /// <summary>The id of one box.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>The id of the other box.</summary>
    public string To { get; set; } = string.Empty;
}
