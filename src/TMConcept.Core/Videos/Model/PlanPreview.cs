namespace TMConcept.Core.Videos;

/// <summary>
/// What the plan preview shows (F10, 0.1.49): the sheet of still frames (a PNG in the run's work folder; empty when it could not be made, the
/// scenes are still listed), the scenes with their key moments, which round of notes this is (1 = the first plan) and whether it is the
/// first scene drawn with its real image ("one scene first").
/// </summary>
public sealed record PlanPreview(string Sheet, IReadOnlyList<PlanPreviewScene> Scenes, int Round, bool FirstScene = false);
