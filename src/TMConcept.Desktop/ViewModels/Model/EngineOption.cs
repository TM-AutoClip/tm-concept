using TMConcept.Core.Engines;

namespace TMConcept.Desktop.ViewModels;

/// <summary>An engine in the picker.</summary>
public sealed record EngineOption(EngineKind Kind, string Label);
