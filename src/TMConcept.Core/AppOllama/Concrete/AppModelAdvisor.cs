using TMConcept.Core.Hardware;

namespace TMConcept.Core.AppOllama;

/// <summary>
/// What a model of the app's own Ollama copy needs and whether it fits a card: graphics memory = its download (weights and vision part) + the
/// KV cache of its context (<see cref="AppModelInfo.KvBytesPerToken"/> × <see cref="AppModelInfo.ContextTokens"/>) +
/// <see cref="AppOllamaDefaults.OverheadBytes"/>; it fits when that is at most the card's own memory. Estimates: Ollama itself measures and
/// moves what does not fit to main memory (slower), it never refuses.
/// </summary>
public static class AppModelAdvisor
{
    private const double Gibibyte = 1024.0 * 1024 * 1024;

    /// <summary>The model of <paramref name="tag"/> in <see cref="AppModelCatalog"/>; null when the app does not offer it.</summary>
    public static AppModelInfo? Find(string tag) => AppModelCatalog.Models.FirstOrDefault(m => string.Equals(m.Tag, tag, StringComparison.Ordinal));

    /// <summary>
    /// The facts of a tag the app does not offer (pulled by an older version or by hand into its folder): its size from the disk, a small
    /// context and a cautious KV figure, no vision.
    /// </summary>
    public static AppModelInfo Unknown(string tag, long bytes) =>
        new(tag, tag, tag.Split(':')[0], bytes, 8_192, 8_192, 64 * 1024, false, false, string.Empty, Models.LicenseUse.Commercial, string.Empty, string.Empty);

    /// <summary>Graphics memory <paramref name="model"/> needs with <paramref name="contextTokens"/> (null = its own context).</summary>
    public static long CardBytes(AppModelInfo model, long? contextTokens = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var context = contextTokens is > 0 ? contextTokens.Value : model.ContextTokens;
        return model.Bytes + (model.KvBytesPerToken * context) + AppOllamaDefaults.OverheadBytes;
    }

    /// <summary>The smallest card class that holds <paramref name="model"/> with its context.</summary>
    public static VramTier Tier(AppModelInfo model) => VramTiers.ForModel(Math.Ceiling(CardBytes(model) / Gibibyte * 10) / 10);

    /// <summary>
    /// Whether <paramref name="model"/> fits a card with <paramref name="cardBytes"/> of own memory (null or 0 = no usable card, or its
    /// memory unknown: the processor).
    /// </summary>
    public static AppModelFit Fit(AppModelInfo model, long? cardBytes)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (cardBytes is not > 0)
        {
            return AppModelFit.Processor;
        }

        return CardBytes(model) <= cardBytes.Value ? AppModelFit.Fits : AppModelFit.TooLarge;
    }

    /// <summary>The recommended models of a card class, best first (<see cref="AppModelCatalog.Recommended"/>).</summary>
    public static IReadOnlyList<AppModelInfo> For(VramTier tier) =>
        AppModelCatalog.Recommended.TryGetValue(tier, out var tags) ? tags.Select(Find).OfType<AppModelInfo>().ToList() : [];
}
