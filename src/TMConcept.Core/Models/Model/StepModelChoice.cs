namespace TMConcept.Core.Models;

/// <summary>
/// The model of a pipeline step for a profile: the last choice made on the page that uses the step. Empty until the user picks one (a paid
/// model is never chosen automatically). Provider <see cref="ModelProviders.Off"/> = the step is turned off (no images, a silent video, no
/// semantic check); an image step that is off can still use the profile's sample images (<see cref="StepOptions.ImageSource"/>).
/// </summary>
public sealed class StepModelChoice
{
    /// <summary>Step code.</summary>
    public string Step { get; set; } = string.Empty;

    /// <summary>The profile the choice belongs to (null only for a page's unsaved choice).</summary>
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
