using TMConcept.Providers.Common;

namespace TMConcept.Providers.Endpoints;

/// <summary>
/// The HTTP layer of user-added local servers, with longer waits than an online provider: a local server loads the model at the first
/// request (minutes for a large model on a consumer graphics card) and then writes far slower than a data centre. Tests replace it with
/// one pointed at a fake server.
/// </summary>
public sealed record EndpointTransport(ProviderTransport Http)
{
    /// <summary>
    /// The real network: the first line may take up to an hour (model load + prompt processing of a long prompt), then 15 minutes of
    /// silence or 3 hours for a whole answer end it (a 64 000-token video code answer at 10 tokens a second takes about 107 minutes).
    /// </summary>
    public static EndpointTransport Network { get; } = new(ProviderTransport.Network with
    {
        Timeout = TimeSpan.FromMinutes(60),
        StreamIdle = TimeSpan.FromMinutes(15),
        StreamLimit = TimeSpan.FromHours(3),
    });

    /// <summary>Longest wait for the model list (the connection test): a server that is up answers it at once, also while a model loads.</summary>
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How many times a 503 (model loading) is retried before the call fails.</summary>
    public int LoadingRetries { get; init; } = 4;

    /// <summary>Wait before the first retry after a 503; it grows with every retry (10, 20, 30, 40 s).</summary>
    public TimeSpan LoadingDelay { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Wait between two readings while a model is being unloaded (Ollama's <c>/api/ps</c>, LM Studio's model list).</summary>
    public TimeSpan UnloadPoll { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Longest wait for an unloaded model to leave the server's list (Ollama itself waits up to 5 s for the card's memory).</summary>
    public TimeSpan UnloadTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Where Ollama and LM Studio are looked for when Settings → API keys opens (their default addresses; tests set none or a fake server's).
    /// </summary>
    public IReadOnlyList<string> DetectAddresses { get; init; } = ["http://127.0.0.1:11434", "http://127.0.0.1:1234"];

    /// <summary>Longest wait for each address of <see cref="DetectAddresses"/> (a server on this computer answers at once).</summary>
    public TimeSpan DetectTimeout { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// How long Ollama keeps a model after a request (<c>keep_alive</c>): long enough for the next batch of the same step, short enough that
    /// a model the app no longer needs leaves the card by itself; the app unloads it at once when another local model needs the card.
    /// </summary>
    public string OllamaKeepAlive { get; init; } = "10m";
}
