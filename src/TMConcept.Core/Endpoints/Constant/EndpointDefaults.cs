namespace TMConcept.Core.Endpoints;

/// <summary>Limits a model of a local server gets when the server does not report them (the user can change both).</summary>
public static class EndpointDefaults
{
    /// <summary>Context window when the server does not say (llama.cpp's and Ollama's usual default is 4 096–8 192).</summary>
    public const long ContextTokens = 8_192;

    /// <summary>Largest answer when not set: a few thousand tokens fit most local context windows next to the prompt.</summary>
    public const int MaxOutputTokens = 4_096;
}
