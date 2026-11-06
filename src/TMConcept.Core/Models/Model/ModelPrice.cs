namespace TMConcept.Core.Models;

/// <summary>
/// New prices of one catalog model (USD per one million tokens unless noted). A missing value keeps the built-in one; a price for a model
/// that is not in the catalog is ignored (prices never add models: what a model can do comes only with the app).
/// </summary>
public sealed class ModelPrice
{
    /// <summary>Provider code.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Model id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Input price.</summary>
    public decimal? InputUsd { get; set; }

    /// <summary>Output price.</summary>
    public decimal? OutputUsd { get; set; }

    /// <summary>Cache read price.</summary>
    public decimal? CacheReadUsd { get; set; }

    /// <summary>Cache write price.</summary>
    public decimal? CacheWriteUsd { get; set; }

    /// <summary>Long-context input price.</summary>
    public decimal? LongInputUsd { get; set; }

    /// <summary>Long-context output price.</summary>
    public decimal? LongOutputUsd { get; set; }

    /// <summary>Price of one image.</summary>
    public decimal? ImageUsd { get; set; }

    /// <summary>Price per one million characters of speech.</summary>
    public decimal? SpeechUsdPerMillionCharacters { get; set; }
}
