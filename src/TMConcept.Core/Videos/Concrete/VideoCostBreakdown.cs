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

    /// <summary>The display order.</summary>
    public static readonly IReadOnlyList<string> Order =
        [PipelineSteps.Concepts, NarrationText, PipelineSteps.ImagePromptCost, PipelineSteps.Images, PipelineSteps.Speech, PipelineSteps.VideoCode];

    /// <summary>
    /// The concept's share of the run it was generated in: the run's cost divided by the concepts it kept (5 concepts for 0.11 $ → 0.022 $
    /// each). Rounded to a millionth of a dollar.
    /// </summary>
    public static decimal ConceptShare(decimal runUsd, int conceptsKept) => conceptsKept <= 0 ? 0 : Math.Round(runUsd / conceptsKept, 6);

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
