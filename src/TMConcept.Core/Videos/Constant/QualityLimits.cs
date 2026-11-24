namespace TMConcept.Core.Videos;

/// <summary>
/// The numbers of the quality modes (F11 + F14, 0.1.50; ours: ReelMimic's production review gave the idea of a frame sheet, a blocker / polish
/// split and a final critic, the numbers are this app's).
/// </summary>
public static class QualityLimits
{
    /// <summary>At most this many reviews of one kind in the careful mode (a repair happens only between two reviews).</summary>
    public const int MaxRounds = 3;

    /// <summary>Polish findings count in the first this many rounds; after them only blockers do.</summary>
    public const int PolishRounds = 2;

    /// <summary>A critic's heading scored below this "does not pass" (scores are 1 to 5).</summary>
    public const int CriticPass = 4;

    /// <summary>The highest score of a critic's heading.</summary>
    public const int CriticMax = 5;

    /// <summary>At most this many frames on the review sheet (more moments: evenly chosen).</summary>
    public const int MaxFrames = 30;

    /// <summary>A scene's first and last frame lie this far inside it (seconds), past the transition.</summary>
    public const double EdgeSeconds = 0.25;

    /// <summary>At most this many findings of one scene are kept from an answer.</summary>
    public const int FindingsPerScene = 3;

    /// <summary>Longest finding kept (characters).</summary>
    public const int FindingMax = 240;

    /// <summary>Longest recurring character description kept (characters, F14).</summary>
    public const int CharacterMax = 300;

    /// <summary>Input tokens a review sheet or picture is estimated at (an image of about 1.2 megapixels).</summary>
    public const long ImageTokens = 1_600;

    /// <summary>Output tokens of a frame or picture review answer, at most.</summary>
    public const int ReviewOutputTokens = 1_500;

    /// <summary>Output tokens of the final critic's answer, at most.</summary>
    public const int CriticOutputTokens = 1_200;

    /// <summary>Output tokens of one scene's new plan in a repair, at most.</summary>
    public const int RepairOutputTokens = 3_000;

    /// <summary>The prompt tokens of one scene's repair besides the plan's system text (the scene, its old plan and the findings).</summary>
    public const long RepairPromptTokens = 1_200;
}
