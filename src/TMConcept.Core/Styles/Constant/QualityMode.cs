namespace TMConcept.Core.Styles;

/// <summary>
/// A template's quality mode (F11, 0.1.50; the developer's decision S2 of 24 Nov 2026). <see cref="Fast"/> is the video as it was made before
/// (no reviewer call). <see cref="Standard"/>: a model that reads images (the "quality check" step, local or online) looks at a sheet of the
/// rendered frames once and a final critic scores the video once; only a report, nothing is changed. <see cref="Careful"/>: the same, plus
/// automatic repair rounds (the scenes it finds faulty are planned again when the app draws the video; local pictures are made again when
/// both the pictures and the reviewer are local). Stored as a number in the template's JSON; a template saved before reads as Fast.
/// </summary>
public enum QualityMode
{
    /// <summary>"Hızlı": no review (the default).</summary>
    Fast = 0,

    /// <summary>"Standart": one frame review and one final critic, a report only.</summary>
    Standard = 1,

    /// <summary>"Özenli": the review, repair rounds and the final critic.</summary>
    Careful = 2,
}
