namespace TMConcept.Core.Videos;

/// <summary>One heading of the final critic (F11, 0.1.50): its score 1 to 5 and the evidence it gives (English, one sentence).</summary>
public sealed record CriticScore(string Heading, int Score, string Note)
{
    /// <summary>Scored below <see cref="QualityLimits.CriticPass"/>: "does not pass" (a report only, nothing is stopped).</summary>
    public bool Fails => Score < QualityLimits.CriticPass;
}
