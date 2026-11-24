namespace TMConcept.Core.Plans;

/// <summary>
/// The numbers of the plan rules (F2, 0.1.41). The 40 % background share is the variety rule of ReelMimic's storyboard format (an idea, no
/// code taken); the others repeat what the plan prompt already asks (<see cref="PlanPromptBuilder.System"/>), now checked by the code.
/// </summary>
public static class PlanLimits
{
    /// <summary>The largest share of the video's length one background may fill (the same image, map region, diagram or the title cards).</summary>
    public const double BackgroundShare = 0.4;

    /// <summary>The background share is checked from this many scenes on (with fewer one scene alone is easily more than 40 %).</summary>
    public const int BackgroundMinScenes = 3;

    /// <summary>Beats one narration sentence may have (the drawer draws one; a second one of the same sentence is merged into it).</summary>
    public const int BeatsPerSentence = 1;

    /// <summary>Words a label may have (a pin's, a label's, a point's or a box's label).</summary>
    public const int LabelWords = 3;

    /// <summary>With subtitles on, nothing with text is placed lower than this (fraction of the height; the bottom quarter is the subtitles').</summary>
    public const double SubtitleTop = 0.75;

    /// <summary>Two camera zooms closer than this are the same shot.</summary>
    public const double SameZoom = 0.05;
}
