using TMConcept.Core.Engines;
using TMConcept.Core.Render;

namespace TMConcept.Core.Videos;

/// <summary>
/// A made version that a new one starts from: its record, its folder, the engine its code is for, the kept code, the input it rendered
/// (scene texts, timing, images) and the narration text of each scene to start an edit from (with its markers when the version was made
/// with an edited text, otherwise the spoken text).
/// </summary>
public sealed record VersionSource(VideoRecord Record, string Folder, IVideoEngine Engine, string Code, RenderInput Input, IReadOnlyList<string> Texts);
