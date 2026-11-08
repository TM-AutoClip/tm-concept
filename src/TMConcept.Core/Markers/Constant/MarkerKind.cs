namespace TMConcept.Core.Markers;

/// <summary>What a placeholder does; every kind is applied by the program itself, whatever the models write (new values are added at the end).</summary>
public enum MarkerKind
{
    /// <summary>
    /// Applied to the narration on this computer: a pause is silence between two spoken parts (and lengthens its scene), a speed marker
    /// changes the voice's speed for the parts after it. Never spoken, never in the subtitles.
    /// </summary>
    Narration = 0,

    /// <summary>
    /// The transition into a scene: a hard cut (also told to the video code) or a short fade through black that ffmpeg adds after the render.
    /// Never spoken, never in the subtitles.
    /// </summary>
    Transition = 1,
}
