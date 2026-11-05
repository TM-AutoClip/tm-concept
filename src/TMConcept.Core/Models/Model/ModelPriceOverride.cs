namespace TMConcept.Core.Models;

/// <summary>A price the user entered for a model that is missing from the catalog or whose price changed.</summary>
public sealed class ModelPriceOverride
{
    /// <summary>Provider code.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Model id.</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>USD per one million input tokens.</summary>
    public decimal InputUsd { get; set; }

    /// <summary>USD per one million output tokens.</summary>
    public decimal OutputUsd { get; set; }

    /// <summary>USD per one million cached input tokens.</summary>
    public decimal CacheReadUsd { get; set; }

    /// <summary>Note.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>Changed (UTC).</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
