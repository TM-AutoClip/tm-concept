using TMConcept.Core.Render;
using TMConcept.Core.Speech;

namespace TMConcept.Core.Videos;

/// <summary>
/// The repair of one scene's plan in the careful mode (F11, 0.1.50; the app's drawer only): the code model that writes plans (it pays), the
/// video's input (the scene's plan as drawn is in it), the template, the scene's number, its spoken spans (null: a silent clip), its image
/// prompt (empty: no picture) and the findings of the scene that counted.
/// </summary>
public sealed record QualityRepairRequest(QualityCall Call, RenderInput Input, Styles.VideoStyle? Style, int Scene, IReadOnlyList<SpeechSpan>? Spans,
    string ImagePrompt, IReadOnlyList<QualityFinding> Findings);
