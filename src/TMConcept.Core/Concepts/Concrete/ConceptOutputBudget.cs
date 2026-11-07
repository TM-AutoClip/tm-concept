using TMConcept.Core.Profiles;

namespace TMConcept.Core.Concepts;

/// <summary>
/// Output size of concepts, from the profile's target length and language (a 180 s Turkish script is about 2.5× a 75 s one, so a fixed
/// number per concept cut long batches off). Used for the first estimate and for the <c>max_tokens</c> of a batch.
/// </summary>
public static class ConceptOutputBudget
{
    /// <summary>Room for thinking on top of the visible answer (adaptive thinking counts against max_tokens).</summary>
    public const int ThinkingHeadroom = 8_000;

    /// <summary>Largest max_tokens asked for one batch (a non-streamed answer must not run for many minutes).</summary>
    public const int Ceiling = 64_000;

    /// <summary>Safety margin on the visible answer.</summary>
    public const double Margin = 1.25;

    /// <summary>Expected visible output tokens of one concept.</summary>
    public static long TokensPerConcept(string language, int seconds)
    {
        var rates = OutputLanguages.Of(language);
        var words = ConceptPromptBuilder.Words(language, Math.Clamp(seconds, ProfileLimits.SecondsMin, ProfileLimits.SecondsMax));
        var characters = (words * rates.CharactersPerWord * (1 + NarrationRate.SceneLineShare)) + NarrationRate.ConceptOverheadCharacters;
        return (long)Math.Ceiling(characters / rates.CharactersPerToken);
    }

    /// <summary>max_tokens of a batch of <paramref name="count"/> concepts.</summary>
    public static int MaxOutputTokens(string language, int seconds, int count) =>
        (int)Math.Min(Ceiling, ThinkingHeadroom + Math.Ceiling(count * TokensPerConcept(language, seconds) * Margin));
}
