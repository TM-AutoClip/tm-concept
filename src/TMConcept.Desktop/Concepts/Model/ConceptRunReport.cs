using TMConcept.Core.Activity;
using TMConcept.Core.Concepts;

namespace TMConcept.Desktop.Concepts;

/// <summary>
/// What a concept run left for the section that started it: the run's result (null when it never started or stopped on an unexpected
/// error), the line for the section's status ("5 concepts generated · 0.041 $ · 2 duplicates thrown away", or why it stopped) and how that
/// line reads (<see cref="ActivityLevel.Error"/> red, <see cref="ActivityLevel.Warning"/> yellow).
/// </summary>
public sealed record ConceptRunReport(ConceptRunResult? Result, string Line, ActivityLevel Level);
