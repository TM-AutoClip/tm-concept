namespace TMConcept.Core.Models;

/// <summary>Options of a step's model (stored as JSON in the step choice).</summary>
public sealed class StepOptions
{
    /// <summary>Effort level (empty = the model's default).</summary>
    public string Effort { get; set; } = string.Empty;

    /// <summary>Most output tokens (0 = the step's default).</summary>
    public int MaxOutputTokens { get; set; }
}
