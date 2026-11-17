namespace TMConcept.Core.AppOllama;

/// <summary>
/// The app's own Ollama server could not be used (<see cref="Code"/>: "missing" = not installed, "start" = it ended while starting or did
/// not answer in time, "port" = no free port). The message says why, with the server's last output lines when it ended.
/// </summary>
public sealed class AppOllamaException : Exception
{
    /// <summary>Creates.</summary>
    public AppOllamaException()
    {
    }

    /// <summary>With a message.</summary>
    public AppOllamaException(string message)
        : base(message)
    {
    }

    /// <summary>With a message and an inner error.</summary>
    public AppOllamaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Failure code.</summary>
    public string Code { get; init; } = "start";
}
