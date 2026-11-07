namespace TMConcept.Storage.Database;

/// <summary>
/// A general step default of 0.1.0–0.1.5 (<c>step_models</c>). Read only by the upgrade to version 3, which copies it into every profile
/// without a choice of its own and removes the table; never created in a new file.
/// </summary>
internal sealed class GeneralStepModelRow
{
    /// <summary>Step code.</summary>
    public string Step { get; set; } = string.Empty;

    /// <summary>Provider code.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Model id.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Key to use.</summary>
    public Guid? KeyId { get; set; }

    /// <summary>Options as JSON.</summary>
    public string OptionsJson { get; set; } = "{}";
}
