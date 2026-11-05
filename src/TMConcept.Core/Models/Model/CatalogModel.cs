using System.Text.Json.Serialization;

namespace TMConcept.Core.Models;

/// <summary>
/// A model of a provider with its prices (USD per one million tokens unless noted) and limits. Comes from the embedded catalog of its
/// provider (prices dated by <see cref="PricedOn"/>) or from a price the user entered.
/// </summary>
public sealed class CatalogModel
{
    /// <summary>Provider code.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Model id sent to the provider.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Capabilities.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ModelCapability>))]
    public ModelCapability Capabilities { get; set; }

    /// <summary>Input price.</summary>
    public decimal InputUsd { get; set; }

    /// <summary>Output price (thinking included).</summary>
    public decimal OutputUsd { get; set; }

    /// <summary>Cache read price (0 = same as input).</summary>
    public decimal CacheReadUsd { get; set; }

    /// <summary>Cache write price (0 = same as input).</summary>
    public decimal CacheWriteUsd { get; set; }

    /// <summary>Prompts above this many tokens are billed at the long-context prices (0 = no such tier).</summary>
    public long LongContextTokens { get; set; }

    /// <summary>Long-context input price.</summary>
    public decimal LongInputUsd { get; set; }

    /// <summary>Long-context output price.</summary>
    public decimal LongOutputUsd { get; set; }

    /// <summary>Price of one generated image (image models).</summary>
    public decimal ImageUsd { get; set; }

    /// <summary>Price per one million characters of speech (speech models).</summary>
    public decimal SpeechUsdPerMillionCharacters { get; set; }

    /// <summary>Context window in tokens.</summary>
    public long ContextTokens { get; set; }

    /// <summary>Largest answer in tokens.</summary>
    public int MaxOutputTokens { get; set; }

    /// <summary>Effort levels the model accepts (empty = none).</summary>
    public List<string> Efforts { get; set; } = [];

    /// <summary>Date of the price table (ISO).</summary>
    public string PricedOn { get; set; } = string.Empty;

    /// <summary>Date the provider retires the model (ISO), empty when unknown.</summary>
    public string RetiresOn { get; set; } = string.Empty;

    /// <summary>Whether the price was entered by the user.</summary>
    public bool UserPrice { get; set; }
}
