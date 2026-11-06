namespace TMConcept.Core.Models;

/// <summary>
/// The model of a pipeline step: the general default (<see cref="ProfileId"/> null) or a profile's override. Empty until the user picks one (a
/// paid model is never chosen automatically). Provider <see cref="ModelProviders.Off"/> = the step is turned off (an override of a
/// profile that wants no images or a silent video while the general default has one).
/// </summary>
public sealed class StepModelChoice
{
    /// <summary>Step code.</summary>
    public string Step { get; set; } = string.Empty;

    /// <summary>The profile this override belongs to; null = the general default.</summary>
    public Guid? ProfileId { get; set; }

    /// <summary>Provider code (<see cref="ModelProviders.Local"/> for a model on this computer, <see cref="ModelProviders.Off"/> = off).</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Model id.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Key to use (null = the first key of the provider).</summary>
    public Guid? KeyId { get; set; }

    /// <summary>Options as JSON (effort, most output tokens, engine, voice, speed).</summary>
    public string OptionsJson { get; set; } = "{}";
}
