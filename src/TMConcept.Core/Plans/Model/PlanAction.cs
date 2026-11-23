namespace TMConcept.Core.Plans;

/// <summary>
/// One overlay action of a beat (<see cref="PlanActions"/>): path and arrow (from an anchor to another, through <see cref="Via"/>, with a
/// <see cref="Walker"/> travelling on it in lively motion), pin, label and highlight at an anchor, country (a map country by its ISO code),
/// counter (a number counting from <see cref="FromValue"/> to <see cref="Value"/>) and text (a short statement as a title). Fields an action
/// does not use stay empty.
/// </summary>
public sealed class PlanAction
{
    /// <summary>The kind (<see cref="PlanActions"/>).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Start point (path, arrow).</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>End point (path, arrow).</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>Points the path goes through, in order.</summary>
    public List<string> Via { get; set; } = [];

    /// <summary>The point of a pin, label, highlight or counter.</summary>
    public string At { get; set; } = string.Empty;

    /// <summary>A short label shown with it (video language).</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The words of a label or a text action.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>What travels along a path in lively motion (<see cref="PlanActions.Walkers"/>).</summary>
    public string Walker { get; set; } = string.Empty;

    /// <summary>A dashed path (a planned or uncertain route).</summary>
    public bool Dashed { get; set; }

    /// <summary>ISO 3166-1 alpha-3 code of a map country (country).</summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>Size of a highlight, a fraction of the shorter side (0 = the default).</summary>
    public double Size { get; set; }

    /// <summary>Where a counter starts.</summary>
    public double? FromValue { get; set; }

    /// <summary>Where a counter ends.</summary>
    public double? Value { get; set; }

    /// <summary>Text after a counter's number ("%", " km").</summary>
    public string Suffix { get; set; } = string.Empty;
}
