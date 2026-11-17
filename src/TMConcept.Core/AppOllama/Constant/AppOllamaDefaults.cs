namespace TMConcept.Core.AppOllama;

/// <summary>
/// Fixed facts of the app's own Ollama copy. Its port is not Ollama's usual one, so it never answers in place of, nor clashes with, an Ollama
/// the user runs (<see cref="UserOllamaPort"/> is never used, not even as a fallback).
/// </summary>
public static class AppOllamaDefaults
{
    /// <summary>The port the app's server tries first (127.0.0.1 only).</summary>
    public const int Port = 11534;

    /// <summary>How many ports from <see cref="Port"/> up are tried before the system is asked for any free one.</summary>
    public const int PortTries = 10;

    /// <summary>The port of an Ollama the user runs: never taken.</summary>
    public const int UserOllamaPort = 11434;

    /// <summary>The only address the app's server listens on.</summary>
    public const string Host = "127.0.0.1";

    /// <summary>The name shown for the app's copy (in the pickers after a model's name, in the logs).</summary>
    public const string Name = "Ollama (TM Concept)";

    /// <summary>The Ollama registry host the models come from (shown in the download list).</summary>
    public const string RegistryHost = "registry.ollama.ai";

    /// <summary>How long the server keeps a model after a request (the app unloads it at once when another local model needs the card).</summary>
    public const string KeepAlive = "10m";

    /// <summary>
    /// Graphics memory Ollama needs beside the weights and the context (its compute buffers and the backend's own share), in bytes: about
    /// 0.5 GiB, an estimate.
    /// </summary>
    public const long OverheadBytes = 512L * 1024 * 1024;

    /// <summary>The longest wait for a just-started server to answer (<c>GET /api/version</c>).</summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The wait between two health checks while the server starts.</summary>
    public static readonly TimeSpan StartPoll = TimeSpan.FromMilliseconds(250);
}
