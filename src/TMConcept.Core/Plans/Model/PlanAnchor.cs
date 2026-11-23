namespace TMConcept.Core.Plans;

/// <summary>
/// A named point of a scene: on a map by <see cref="Lon"/> and <see cref="Lat"/>, on an image by <see cref="X"/> and <see cref="Y"/>
/// (fractions of the frame). On an image the plan's guess is replaced by where a vision model sees <see cref="Find"/> when one is chosen
/// (<see cref="ImagePointPrompt"/>). <see cref="Label"/> is shown by a pin or a label.
/// </summary>
public sealed class PlanAnchor
{
    /// <summary>Id the actions use.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Short label, in the video's language (may be empty).</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>What to look for in the scene's image, in English ("the rope bridge"); empty on maps and diagrams.</summary>
    public string Find { get; set; } = string.Empty;

    /// <summary>Longitude (map scenes).</summary>
    public double? Lon { get; set; }

    /// <summary>Latitude (map scenes).</summary>
    public double? Lat { get; set; }

    /// <summary>Across the frame, 0 = left, 1 = right (image scenes).</summary>
    public double? X { get; set; }

    /// <summary>Down the frame, 0 = top, 1 = bottom (image scenes).</summary>
    public double? Y { get; set; }
}
