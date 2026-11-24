namespace TMConcept.Core.Videos;

/// <summary>Why the review rounds ended (F11, 0.1.50; kept in the version's quality report).</summary>
public static class QualityStops
{
    /// <summary>The last review found nothing that counts.</summary>
    public const string Clean = "clean";

    /// <summary>A report only (standard mode): one review, no repair.</summary>
    public const string ReportOnly = "report";

    /// <summary>The round limit (<see cref="QualityLimits.MaxRounds"/>) was reached.</summary>
    public const string Limit = "limit";

    /// <summary>The list of findings did not get shorter after a repair: repairing more would only cost.</summary>
    public const string Stuck = "stuck";

    /// <summary>Nothing could be repaired (the model's free code, or no scene could be planned again): findings are only reported.</summary>
    public const string NoRepair = "no-repair";

    /// <summary>The review could not run (the sheet could not be made, the reviewer's answer could not be read).</summary>
    public const string Skipped = "skipped";
}
