using TMConcept.Core.Models;
using TMConcept.Core.Speech;

namespace TMConcept.Core.Videos;

/// <summary>
/// What a video drawn by the app (0.1.33) needs beyond the code call: each scene's spoken spans (the beats follow them; empty = a silent or
/// kept clip, the caption's sentences are used), each scene's image prompt (the plan knows what the picture shows) and the optional image
/// points model.
/// </summary>
public sealed record PlanContext(IReadOnlyList<IReadOnlyList<SpeechSpan>?> Spans, IReadOnlyList<string> ImagePrompts, ResolvedModel? PointsModel)
{
    /// <summary>
    /// A library music the concept chooses (0.1.45, <see cref="PreparedMusic.Automatic"/>): the plan call writes each scene's music hint and
    /// the track is chosen and laid under the scenes before the render (<see cref="RenderOutcome.Music"/>); null otherwise.
    /// </summary>
    public PreparedMusic? Music { get; init; }

    /// <summary>The narration clips of the scenes (their silence after the last word may be cut to meet a beat); empty = none known.</summary>
    public IReadOnlyList<Speech.SpeechClip?> Clips { get; init; } = [];

    /// <summary>
    /// F10 (0.1.49): the scenes have their plans already (made and approved before the images, <see cref="VideoAssembler.DraftPlanAsync"/>):
    /// no plan call, only the image points are placed.
    /// </summary>
    public bool Planned { get; init; }

    /// <summary>Nothing known: the captions' sentences, the scenes' own lines, no image points model.</summary>
    public PlanContext()
        : this([], [], null)
    {
    }
}
