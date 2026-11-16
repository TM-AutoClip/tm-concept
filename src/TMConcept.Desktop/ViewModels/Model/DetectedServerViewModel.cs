using TMConcept.Core.Endpoints;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A server found on this computer (Ollama, LM Studio) and not saved yet, with its line for Settings → API keys.</summary>
public sealed record DetectedServerViewModel(DetectedServer Server, string Label);
