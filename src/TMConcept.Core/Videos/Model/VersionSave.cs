using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Speech;

namespace TMConcept.Core.Videos;

/// <summary>
/// What a finished run writes as a version: the rendered project and silent video in the work folder, the input it rendered, one clip per
/// scene (null = silent scene), the target version folder, and what the record says (engine, models, settings, warnings, costs).
/// <see cref="ExtraCosts"/> are cost lines outside the video's own total (the concept share of the first version); <see cref="Watch"/> runs
/// since the run started (the record keeps the time up to the thumbnail).
/// </summary>
public sealed record VersionSave(Guid Id, Profile Profile, Guid? ConceptId, string Title, string Work, RenderOutcome Render, IReadOnlyList<SpeechClip?> Clips,
    VersionTarget Target, string TargetFolder, VideoOptions Options, string EngineName, string CodeModel, string SpeechModel, string ImageModel, string PromptVersion,
    IReadOnlyList<string> Warnings, IReadOnlyList<VideoCost> Spent, IReadOnlyList<VideoCost> ExtraCosts, System.Diagnostics.Stopwatch Watch)
{
    /// <summary>The input the video was rendered with.</summary>
    public RenderInput Input => Render.Request.Input;

    /// <summary>The edited scene texts with their markers (a new voice), kept as <see cref="VideoFolders.ScriptFile"/>; null = none.</summary>
    public IReadOnlyList<string>? Script { get; init; }
}
