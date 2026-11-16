using System.Text.Json.Serialization;

namespace TMConcept.Core.Models;

/// <summary>
/// A model of a provider with its prices (USD per one million tokens unless noted) and limits. Comes from the embedded catalog of its
/// provider (prices dated by <see cref="PricedOn"/>), with prices updated from <see cref="PriceSource"/>. Local models (provider
/// <see cref="ModelProviders.Local"/>) are free and name the components they need (<see cref="Requires"/>).
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

    /// <summary>Where the price comes from (<see cref="PriceSources"/>; empty = the built-in table).</summary>
    public string PriceSource { get; set; } = string.Empty;

    /// <summary>Components a local model needs on this computer (installed on save after a confirmation).</summary>
    public List<string> Requires { get; set; } = [];

    /// <summary>
    /// Least graphics card memory (GB) a local model needs to run well (0 = no graphics card needed): its graphics card class
    /// (<see cref="Hardware.VramTiers.ForModel"/>; a card sees its own class and every lower one). A warning, never a block.
    /// </summary>
    public int MinVideoMemoryGb { get; set; }

    /// <summary>Main memory (GB) a local model needs besides (0 = unknown or small); used when its weights move off the graphics card.</summary>
    public int MemoryGb { get; set; }

    /// <summary>
    /// A local model's license code (<c>apache-2.0</c>, <c>flux-non-commercial</c>, <c>cpml</c>, <c>cc-by-nc-4.0</c>…; empty = nothing to say);
    /// the picker names it (<c>license.{code}</c> in the language files).
    /// </summary>
    public string License { get; set; } = string.Empty;

    /// <summary>
    /// What a local model's license allows (<see cref="Models.LicenseUse"/>): anything but commercial use is offered WITH a clear warning and
    /// asked before the first download (0.1.17, the developer), images and voices alike.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<LicenseUse>))]
    public LicenseUse LicenseUse { get; set; }

    /// <summary>Where the license text is (the model's own page), shown in the license question.</summary>
    public string LicenseUrl { get; set; } = string.Empty;
}
