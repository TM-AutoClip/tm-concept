namespace TMConcept.Core.Concepts;

/// <summary>Limits of concepts and of a generation run.</summary>
public static class ConceptLimits
{
    /// <summary>Fewest concepts in one request.</summary>
    public const int CountMin = 1;

    /// <summary>Most concepts in one request.</summary>
    public const int CountMax = 100;

    /// <summary>Concepts per model call (a request of 50 runs as 20 + 20 + 10).</summary>
    public const int BatchSize = 20;

    /// <summary>Longest title.</summary>
    public const int TitleMax = 90;

    /// <summary>Longest title accepted from a model before it is cut.</summary>
    public const int TitleHardMax = 160;

    /// <summary>Longest summary.</summary>
    public const int SummaryMax = 600;

    /// <summary>Longest content (voice-over script with scene lines).</summary>
    public const int ContentMax = 12000;

    /// <summary>Longest extra prompt.</summary>
    public const int ExtraPromptMax = 1000;
}
