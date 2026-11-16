namespace TMConcept.Core.Endpoints;

/// <summary>
/// A model a server lists. <paramref name="ContextTokens"/> is the context to use (the running one of llama-server, a proposal within the
/// model's trained context for Ollama and LM Studio, which the app loads with it); 0 when the server does not say. The rest is filled only by
/// servers whose own API says it (Ollama, LM Studio): a display <paramref name="Name"/>, whether it reads images, whether it can think
/// (Ollama's <c>think</c> switch), whether the server takes a JSON schema for it, and its size on disk.
/// </summary>
public sealed record ListedModel(string Id, long ContextTokens, string Name = "", bool Vision = false, bool CanThink = false, bool JsonSchema = false,
    long SizeBytes = 0);
