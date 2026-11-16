namespace TMConcept.Core.Endpoints;

/// <summary>Limits a model of a local server gets when the server does not report them (the user can change both).</summary>
public static class EndpointDefaults
{
    /// <summary>Context window when the server does not say (llama.cpp's and Ollama's usual default is 4 096–8 192).</summary>
    public const long ContextTokens = 8_192;

    /// <summary>Largest answer of a small context: a few thousand tokens fit most local context windows next to the prompt.</summary>
    public const int MaxOutputTokens = 4_096;

    /// <summary>
    /// Room kept for the prompt when the answer limit is taken from the context: the concept and video code prompts are a few thousand
    /// tokens. The rest of a large context goes to the answer, since the video code answer runs to tens of thousands of tokens (a fixed
    /// 4 096 cut every video code answer off, the 0.1.16 review).
    /// </summary>
    public const long PromptReserveTokens = 8_192;

    /// <summary>
    /// Context proposed for an Ollama or LM Studio model (within its trained context): the app loads the model with it, and a larger context
    /// takes more graphics memory (the user can raise it for long video code answers).
    /// </summary>
    public const long ServerContextTokens = 16_384;
}
