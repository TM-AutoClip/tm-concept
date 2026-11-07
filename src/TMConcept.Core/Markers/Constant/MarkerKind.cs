namespace TMConcept.Core.Markers;

/// <summary>Where a marker is offered (new values are added at the end).</summary>
public enum MarkerKind
{
    /// <summary>
    /// In a prompt (the profile's channel concept, the extra request): a style request the model reads with the meaning the prompt gives it
    /// (transitions, pauses, emphasis…; the video code later draws what the concept's scene lines say).
    /// </summary>
    Prompt = 0,

    /// <summary>
    /// In a narration text edited for a new version: applied on this computer without a model (a pause is silence between two spoken parts,
    /// a speed marker changes the voice's speed for the parts after it). Never spoken, never in the subtitles.
    /// </summary>
    Narration = 1,
}
