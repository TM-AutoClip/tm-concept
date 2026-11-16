namespace TMConcept.Core.Endpoints;

/// <summary>
/// What a user-added server is: a plain OpenAI-compatible one (only <c>chat/completions</c>), or one whose own API the app speaks too, so it
/// can set the context, list the models with their limits and unload a model when another one needs the graphics card (0.1.17).
/// </summary>
public enum EndpointKind
{
    /// <summary>Any OpenAI-compatible server (Lemonade, llama-server, vLLM, a remote service): no load or unload control.</summary>
    Generic = 0,

    /// <summary>Ollama: native <c>/api/chat</c> (context sent with every request), unload with <c>keep_alive: 0</c>, memory from <c>/api/ps</c>.</summary>
    Ollama = 1,

    /// <summary>LM Studio 0.4+: loaded with the chosen context through <c>/api/v1/models/load</c>, unloaded through <c>/api/v1/models/unload</c>.</summary>
    LmStudio = 2,
}
