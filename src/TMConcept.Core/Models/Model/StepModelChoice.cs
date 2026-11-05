namespace TMConcept.Core.Models;

/// <summary>The default model of a pipeline step (empty until the user picks one: a paid model is never chosen automatically).</summary>
public sealed class StepModelChoice
{
    /// <summary>Step code.</summary>
    public string Step { get; set; } = string.Empty;

    /// <summary>Provider code.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Model id.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Key to use (null = the first key of the provider).</summary>
    public Guid? KeyId { get; set; }

    /// <summary>Options as JSON (effort, temperature, most output tokens).</summary>
    public string OptionsJson { get; set; } = "{}";
}
