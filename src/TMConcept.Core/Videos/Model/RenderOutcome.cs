using TMConcept.Core.Engines;

namespace TMConcept.Core.Videos;

/// <summary>A rendered silent video: the request it was rendered with (project folder, code, input, output) and whether the code was repaired.</summary>
public sealed record RenderOutcome(EngineRenderRequest Request, bool Repaired);
