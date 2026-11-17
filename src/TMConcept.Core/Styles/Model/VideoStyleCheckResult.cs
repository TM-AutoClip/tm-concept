namespace TMConcept.Core.Styles;

/// <summary>The outcome of <see cref="VideoStyleCheck.Check"/>.</summary>
/// <param name="Code">The code with every colour outside the palette replaced by its nearest palette colour (unchanged when none was).</param>
/// <param name="Findings">What breaks the template (empty = passed); each one goes to the single repair like a safety finding.</param>
/// <param name="Fixes">The colours that were replaced ("#ff0000 → #E5383B (accent)"), for the log.</param>
public sealed record VideoStyleCheckResult(string Code, IReadOnlyList<string> Findings, IReadOnlyList<string> Fixes);
