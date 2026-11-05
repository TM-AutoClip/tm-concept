using TMConcept.Core.Models;

namespace TMConcept.Core.Pipeline;

/// <summary>A step that calls a model, with the capability it needs and the recommended models ("provider/model"; shown, never auto-picked).</summary>
public sealed record PipelineStep(string Code, ModelCapability Capability, IReadOnlyList<string> Recommended);
