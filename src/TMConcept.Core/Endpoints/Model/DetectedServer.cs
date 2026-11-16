namespace TMConcept.Core.Endpoints;

/// <summary>
/// A server found on this computer at its usual address (Ollama on port 11434, LM Studio on 1234): its kind, the address to save
/// (without <c>/v1</c>), its version when it says one and how many text models it has.
/// </summary>
public sealed record DetectedServer(EndpointKind Kind, string Address, string Version, int ModelCount);
