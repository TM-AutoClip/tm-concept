namespace TMConcept.Core.Costs;

/// <summary>Rules of thumb for estimates before a call (corrected by measured averages once there is history).</summary>
public static class TokenRules
{
    /// <summary>Characters per token in Turkish text (rough; newer tokenizers count more tokens).</summary>
    public const double TurkishCharactersPerToken = 2.6;

    /// <summary>Characters per token in English text.</summary>
    public const double EnglishCharactersPerToken = 3.6;
}
