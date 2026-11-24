using System.Globalization;

namespace TMConcept.Core.Videos;

/// <summary>One finding of the reviewer (F11, 0.1.50): its scene (1-based), how bad it is and what it is (English, one sentence).</summary>
public sealed record QualityFinding(int Scene, QualitySeverity Severity, string Problem)
{
    /// <summary>For the log and the report: "scene 2 (blocker): …".</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"scene {Scene} ({(Severity == QualitySeverity.Blocker ? "blocker" : "polish")}): {Problem}");
}
