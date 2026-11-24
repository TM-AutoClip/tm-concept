using TMConcept.Core.Engines;
using TMConcept.Core.Models;
using TMConcept.Core.Speech;

namespace TMConcept.Core.Videos;

/// <summary>
/// The quality review of a rendered video (F11, 0.1.50; <see cref="QualityStep.AfterRenderAsync"/>): the reviewer (null: the quality check
/// step is off or not ready), the code model (it writes a repaired scene's plan), the engine and settings, the render, the work folder, every
/// scene's spoken spans and image prompt (for a repaired plan), the run's progress, the video's id and the report it fills.
/// </summary>
public sealed record RenderQualityJob(ResolvedModel? Reviewer, ResolvedModel CodeModel, IVideoEngine Engine, VideoOptions Options, RenderOutcome Render,
    string Work, IReadOnlyList<IReadOnlyList<SpeechSpan>?> Spans, IReadOnlyList<string> ImagePrompts, VideoStepProgress Steps, Guid VideoId,
    QualityReport Report);
