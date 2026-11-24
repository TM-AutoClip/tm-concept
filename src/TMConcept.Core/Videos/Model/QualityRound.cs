namespace TMConcept.Core.Videos;

/// <summary>One review round (F11, 0.1.50): its number, the findings that counted in it and the scenes repaired after it.</summary>
public sealed class QualityRound
{
    /// <summary>The round's number (1-based).</summary>
    public int Round { get; set; }

    /// <summary>The findings that counted (after round <see cref="QualityLimits.PolishRounds"/> only blockers).</summary>
    public List<QualityFinding> Findings { get; set; } = [];

    /// <summary>The scenes repaired after this review (empty: none).</summary>
    public List<int> Repaired { get; set; } = [];
}
