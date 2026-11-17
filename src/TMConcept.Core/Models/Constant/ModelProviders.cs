namespace TMConcept.Core.Models;

/// <summary>Provider codes that are not online providers.</summary>
public static class ModelProviders
{
    /// <summary>Models that run on this computer (local voices, image models, embedding): free, no key, installed when first used.</summary>
    public const string Local = "local";

    /// <summary>
    /// The kind of a user-added local server (OpenAI-compatible): its models have the provider code <c>endpoint:&lt;id&gt;</c>
    /// (<see cref="Endpoints.EndpointCodes"/>). Not <see cref="Local"/>: that means models this app installs and runs itself.
    /// </summary>
    public const string Endpoint = "endpoint";

    /// <summary>
    /// The models of the app's own Ollama copy ("Ollama (TM Concept)", 0.1.20): pulled by the app into its own models folder, run by the app's
    /// own server on a port of its own (never the user's Ollama on 11434). Free, no key; the model id is the Ollama tag
    /// (<c>qwen3.5:4b-q4_K_M</c>). See <see cref="TMConcept.Core.AppOllama.AppModelRegistry"/>.
    /// </summary>
    public const string AppOllama = "app-ollama";

    /// <summary>An optional step turned off explicitly.</summary>
    public const string Off = "off";
}
