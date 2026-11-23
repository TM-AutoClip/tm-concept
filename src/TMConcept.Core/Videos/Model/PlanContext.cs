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
    /// <summary>Nothing known: the captions' sentences, the scenes' own lines, no image points model.</summary>
    public PlanContext()
        : this([], [], null)
    {
    }
}
