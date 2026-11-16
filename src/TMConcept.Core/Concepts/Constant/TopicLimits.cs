namespace TMConcept.Core.Concepts;

/// <summary>Limits of "Suggest topics" and of one topic.</summary>
public static class TopicLimits
{
    /// <summary>Fewest topics one press asks for.</summary>
    public const int CountMin = 5;

    /// <summary>
    /// Most topics one press asks for: one call, no batching (100 Turkish topics are about 4 500 visible tokens, well under
    /// <see cref="TopicSuggester.OutputMax"/> with thinking at effort low).
    /// </summary>
    public const int CountMax = 100;

    /// <summary>Topics asked for when the user changed nothing.</summary>
    public const int CountDefault = 20;

    /// <summary>Longest topic kept.</summary>
    public const int TopicMax = 120;

    /// <summary>Smallest answer limit (max_tokens) the setting allows: room for thinking and a few topics.</summary>
    public const int OutputMin = 2_000;

    /// <summary>Largest answer limit (max_tokens) the setting allows (a model's own limit lowers it further).</summary>
    public const int OutputLimit = 128_000;
}
