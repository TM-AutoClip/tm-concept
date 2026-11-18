namespace TMConcept.Desktop.Catalog;

/// <summary>
/// A model of a cloud provider the app already knows (its own catalog has models of that provider): the same fields as the built-in
/// catalog's entries, restricted to what that provider's built-in models use. Prices are the fallback; the prices file still updates them.
/// </summary>
public sealed class RemoteProviderModel
{
    /// <summary>Provider code (<c>openai</c>, <c>anthropic</c> …).</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The provider's model id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What it can do (<c>Chat, Schema, Vision</c>), only kinds that provider's built-in models have.</summary>
    public string Capabilities { get; set; } = string.Empty;

    /// <summary>Context window in tokens.</summary>
    public long ContextTokens { get; set; }

    /// <summary>Longest answer in tokens.</summary>
    public int MaxOutputTokens { get; set; }

    /// <summary>Reasoning efforts, only ones that provider's built-in models use.</summary>
    public List<string>? Efforts { get; set; }

    /// <summary>USD per million input tokens.</summary>
    public decimal InputUsd { get; set; }

    /// <summary>USD per million output tokens.</summary>
    public decimal OutputUsd { get; set; }

    /// <summary>USD per million cached input tokens.</summary>
    public decimal CacheReadUsd { get; set; }

    /// <summary>USD per image.</summary>
    public decimal ImageUsd { get; set; }

    /// <summary>USD per million characters of speech.</summary>
    public decimal SpeechUsdPerMillionCharacters { get; set; }

    /// <summary>When the provider retires it (<c>yyyy-MM-dd</c>), empty when not known.</summary>
    public string RetiresOn { get; set; } = string.Empty;
}
