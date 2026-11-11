namespace TMConcept.Desktop.Localization;

/// <summary>
/// This month's spending: the total (concept runs, topic and edit calls, the made videos and what failed videos paid) and its parts in the
/// current language ("anthropic 0.80 $ · videos 0.40 $"; empty when nothing was paid).
/// </summary>
public sealed record Spending(decimal TotalUsd, string Breakdown);
