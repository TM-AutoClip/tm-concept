using TMConcept.Core.Models;
using TMConcept.Core.Render;
using TMConcept.Core.Speech;

namespace TMConcept.Core.Plans;

/// <summary>
/// One scene plan of a video (<see cref="ScenePlanner"/>): the code model that writes it, the optional model that places the image points,
/// the timed input (its scenes get the plans), the template, each scene's spoken spans (null for a silent or kept clip), each scene's image
/// prompt (empty when unknown), the project folder the images are in and the video's id (for its costs).
/// </summary>
public sealed record PlanRequest(ResolvedModel CodeModel, ResolvedModel? PointsModel, RenderInput Input, Styles.VideoStyle? Style,
    IReadOnlyList<IReadOnlyList<SpeechSpan>?> Spans, IReadOnlyList<string> ImagePrompts, string ProjectFolder, Guid VideoId);
