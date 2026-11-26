using TMConcept.Core.Processes;

namespace TMConcept.Speech.Music;

/// <summary>
/// Why a rakkase run failed, from its last lines (0.1.55): rakkase writes its plan check's findings in Turkish (<c>"  HATA: zorunlu alan yok:
/// bpm"</c>, <c>"DENETIM SONUC: …"</c>) and ends with its own code (<c>[RK-E-PLAN] …</c>). The app's general summary
/// (<see cref="ChildOutput.Summary"/>) looks for English error words only and kept just the last line, which says "see the lines above".
/// These lines are kept in their order, on one line; without any of them the general summary is used.
/// </summary>
public static class RakkaseOutput
{
    /// <summary>The reason of a failed run, at most <see cref="RakkasePlanRules.ReasonCharacters"/> characters.</summary>
    public static string Reason(string? lines)
    {
        var all = (lines ?? string.Empty).Split('\n').Select(l => l.Trim().TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        var chosen = all.Where(l => l.StartsWith(RakkasePlanRules.ErrorMark, StringComparison.Ordinal)
                || l.StartsWith(RakkasePlanRules.ResultMark, StringComparison.Ordinal)
                || l.Contains(RakkasePlanRules.CodeMark, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (chosen.Count == 0)
        {
            return ChildOutput.Summary(lines);
        }

        // The same scrubbing as every child program's output (the home folder as ~), then the cut of the screen's reason.
        return ChildOutput.Summary(string.Join(" | ", chosen), lines: 1, characters: RakkasePlanRules.ReasonCharacters);
    }
}
