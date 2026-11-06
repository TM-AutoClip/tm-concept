namespace TMConcept.Core.Models;

/// <summary>Where the prices of the catalog come from.</summary>
public static class PriceSources
{
    /// <summary>The table shipped in the app (fallback, also offline).</summary>
    public const string BuiltIn = "built-in";

    /// <summary>The prices file of the public repository (pricing/prices.json), read at most once a day.</summary>
    public const string Repository = "repository";

    /// <summary>OpenRouter's public models list (OpenRouter models only), read at most once a day.</summary>
    public const string OpenRouter = "openrouter";
}
