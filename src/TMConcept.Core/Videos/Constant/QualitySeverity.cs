namespace TMConcept.Core.Videos;

/// <summary>How bad a reviewer's finding is (F11, 0.1.50; ReelMimic's blocker / polish split, our rules).</summary>
public enum QualitySeverity
{
    /// <summary>Seen at normal speed: it spoils the scene. Counted in every round.</summary>
    Blocker = 0,

    /// <summary>Only seen when the frame is enlarged. Counted in the first <see cref="QualityLimits.PolishRounds"/> rounds only.</summary>
    Polish = 1,
}
