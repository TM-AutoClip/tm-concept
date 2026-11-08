using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>
/// Output size of concepts. <see cref="MaxOutputTokens"/> is the <c>max_tokens</c> of a batch: the model's own limit, at most
/// <see cref="Ceiling"/>, whatever the count (0.1.7 gave a batch only 8 000 tokens for thinking plus its visible text × 1.25; Sonnet 5.5 at
/// effort high thought past that and cut five concepts after the second, 8 Nov 2026). The call streams and only what is produced is paid
/// for, so a high limit is not a reserve, only a roof. <see cref="TokensPerConcept"/> (from the profile's target length and language: a
/// 180 s Turkish script is about 2.5× a 75 s one) and <see cref="ThinkingShare"/> feed the estimate.
/// </summary>
public static class ConceptOutputBudget
{
    /// <summary>Largest max_tokens asked for one batch (thinking included; the developer's decision: always 64 000, lowered to the model's limit).</summary>
    public const int Ceiling = 64_000;

    /// <summary>Expected visible output tokens of one concept.</summary>
    public static long TokensPerConcept(string language, int seconds)
    {
        var rates = OutputLanguages.Of(language);
        var words = ConceptPromptBuilder.Words(language, Math.Clamp(seconds, ProfileLimits.SecondsMin, ProfileLimits.SecondsMax));
        var characters = (words * rates.CharactersPerWord * (1 + NarrationRate.SceneLineShare)) + NarrationRate.ConceptOverheadCharacters;
        return (long)Math.Ceiling(characters / rates.CharactersPerToken);
    }

    /// <summary>max_tokens of a batch with <paramref name="model"/>: <see cref="Ceiling"/>, or the model's limit when it is smaller.</summary>
    public static int MaxOutputTokens(Models.CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.MaxOutputTokens > 0 ? Math.Min(Ceiling, model.MaxOutputTokens) : Ceiling;
    }

    /// <summary>
    /// Concepts per batch: as many as fit <paramref name="maxTokens"/> when each takes <paramref name="perConceptHigh"/> output tokens (the
    /// high end, thinking included), at least 1 and at most <see cref="ConceptLimits.BatchSize"/>.
    /// </summary>
    public static int BatchSize(int maxTokens, double perConceptHigh) =>
        perConceptHigh <= 0 ? ConceptLimits.BatchSize : (int)Math.Clamp(Math.Floor(maxTokens / perConceptHigh), 1, ConceptLimits.BatchSize);

    /// <summary>
    /// The whole output (thinking included) as a multiple of the visible text, low and high end, by effort. A model without effort levels
    /// thinks little or not at all. "Automatic" (empty) counts as high: that is the default of the Claude models. Measured on 8 Nov 2026:
    /// Sonnet 5.5 at high spent about 4–6× the visible text.
    /// </summary>
    public static (double Low, double High) ThinkingShare(Models.CatalogModel model, string effort)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Efforts.Count == 0)
        {
            return (1.1, 1.5);
        }

        return effort switch
        {
            "low" => (1.2, 2.0),
            "medium" => (1.5, 3.5),
            "xhigh" => (3.5, 8.0),
            "max" => (4.0, 10.0),
            _ => (2.5, 6.0),
        };
    }
}
