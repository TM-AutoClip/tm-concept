using TMConcept.Core.Models;

namespace TMConcept.Core.Pipeline;

/// <summary>
/// A step that calls a model, with the capability it needs, the recommended models ("provider/model"; shown, never auto-picked) and whether
/// it can be turned off (an optional step that is not set is off; a required one without a model stops the work that needs it).
/// </summary>
public sealed record PipelineStep(string Code, ModelCapability Capability, IReadOnlyList<string> Recommended, bool Optional);
