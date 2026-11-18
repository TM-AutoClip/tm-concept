using TMConcept.Core.Pipeline;

namespace TMConcept.Core.Videos;

/// <summary>
/// The cost details of a video in the order the work happens: the concept's share of its generation run, the narration text and its
/// split into scenes (done on this computer, free; shown so every step is there), the image prompts, the images, the narration audio, the
/// video code (with its repair), then the total. The lines of one step are added up (one line per image or per code call is stored).
/// </summary>
public static class VideoCostBreakdown
{
    /// <summary>Pseudo step of the narration text and the scene split (no model, no cost).</summary>
    public const string NarrationText = "narration-text";

    /// <summary>
    /// Cost label of the AI edits of the video's concept (0.1.25: one line per edit run, stored with the first video made after the edits).
    /// Outside the video's own total like the concept share: the edit runs are counted once, in the month's spending. Videos made before
    /// 0.1.25 have none (their edits were not linked to the concept; never guessed).
    /// </summary>
    public const string ConceptEdits = "concept-edits";

    /// <summary>The display order.</summary>
    public static readonly IReadOnlyList<string> Order =
        [PipelineSteps.Concepts, ConceptEdits, NarrationText, PipelineSteps.ImagePromptCost, PipelineSteps.Images, PipelineSteps.Speech, PipelineSteps.VideoCode];

    /// <summary>
    /// The concept's share of the run it was generated in: the run's cost divided by the concepts it kept (5 concepts for 0.11 $ → 0.022 $
    /// each). Rounded to a millionth of a dollar.
    /// </summary>
    public static decimal ConceptShare(decimal runUsd, int conceptsKept) => conceptsKept <= 0 ? 0 : Math.Round(runUsd / conceptsKept, 6);

    /// <summary>
    /// The concept share of a video for its cost details: the stored line when there is one; for a later version, counted with the first
    /// one; else worked out from the concept's generation run while it is still there (a run that cost nothing stores no line); a concept
    /// written by hand has none; anything else is unknown (never guessed). <paramref name="concept"/> and <paramref name="run"/> are the video's
    /// concept and that concept's run when they still exist.
    /// </summary>
    public static ConceptShareLine Share(IReadOnlyList<VideoCost> costs, Concepts.Concept? concept, Runs.Run? run, bool firstVersion)
    {
        ArgumentNullException.ThrowIfNull(costs);
        var known = run is not null && concept?.RunId == run.Id;
        decimal? runUsd = known ? run!.ActualUsd : null;
        int? kept = known ? run!.Accepted : null;
        var stored = costs.Where(c => c.Step == PipelineSteps.Concepts).ToList();
        if (stored.Count > 0)
        {
            return new ConceptShareLine(ConceptShareKind.Recorded, stored.Sum(c => c.Usd), stored.All(c => c.Exact), runUsd, kept);
        }

        if (!firstVersion)
        {
            return new ConceptShareLine(ConceptShareKind.FirstVersion, 0, true, runUsd, kept);
        }

        if (concept is { RunId: null })
        {
            return new ConceptShareLine(ConceptShareKind.Handwritten, 0, true, null, null);
        }

        return known
            ? new ConceptShareLine(ConceptShareKind.FromRun, ConceptShare(run!.ActualUsd, run.Accepted), run.CostExact, runUsd, kept)
            : new ConceptShareLine(ConceptShareKind.Unknown, 0, false, null, null);
    }

    /// <summary>What the AI edits of the video's concept cost together (0 when it has none recorded).</summary>
    public static decimal EditsUsd(IReadOnlyList<VideoCost> costs)
    {
        ArgumentNullException.ThrowIfNull(costs);
        return costs.Where(c => c.Step == ConceptEdits).Sum(c => c.Usd);
    }

    /// <summary>The lines in <see cref="Order"/> (steps the video did not use are left out; the free narration line is always there).</summary>
    public static IReadOnlyList<VideoCostLine> Lines(IReadOnlyList<VideoCost> costs)
    {
        ArgumentNullException.ThrowIfNull(costs);
        var lines = costs.GroupBy(c => c.Step, StringComparer.Ordinal)
            .Select(g => new VideoCostLine(g.Key, g.Sum(c => c.Usd), g.All(c => c.Exact), g.Count()))
            .Append(new VideoCostLine(NarrationText, 0, true, 0));
        return lines.OrderBy(l => Rank(l.Step)).ThenBy(l => l.Step, StringComparer.Ordinal).ToList();
    }

    // A step that is not in Order (a newer one) comes after the known ones.
    private static int Rank(string step)
    {
        for (var i = 0; i < Order.Count; i++)
        {
            if (string.Equals(Order[i], step, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return Order.Count;
    }
}
