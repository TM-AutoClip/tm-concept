namespace TMConcept.Core.Plans;

/// <summary>The overlay actions the drawer knows (anything else is dropped by <see cref="PlanReader"/>).</summary>
public static class PlanActions
{
    /// <summary>A route drawn on progressively, with an arrow head.</summary>
    public const string Path = "path";

    /// <summary>A straight arrow.</summary>
    public const string Arrow = "arrow";

    /// <summary>A pin with its label.</summary>
    public const string Pin = "pin";

    /// <summary>A label.</summary>
    public const string Label = "label";

    /// <summary>A glowing ring around a spot.</summary>
    public const string Highlight = "highlight";

    /// <summary>A map country filled in.</summary>
    public const string Country = "country";

    /// <summary>A counting number.</summary>
    public const string Counter = "counter";

    /// <summary>A short statement shown as a title.</summary>
    public const string Text = "text";

    /// <summary>Every action.</summary>
    public static readonly IReadOnlyList<string> All = [Path, Arrow, Pin, Label, Highlight, Country, Counter, Text];

    /// <summary>What may travel along a path.</summary>
    public static readonly IReadOnlyList<string> Walkers = ["person", "army", "ship", "caravan", "rider", "dot"];
}
