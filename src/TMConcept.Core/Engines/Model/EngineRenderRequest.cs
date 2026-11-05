using TMConcept.Core.Render;

namespace TMConcept.Core.Engines;

/// <summary>A render: work folder (everything is written only inside it), the model's code, the input, the output file and the workers.</summary>
public sealed record EngineRenderRequest(string Folder, string Code, RenderInput Input, string Output, int Workers);
