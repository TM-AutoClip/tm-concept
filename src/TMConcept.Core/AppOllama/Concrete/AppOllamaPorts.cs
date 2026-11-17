namespace TMConcept.Core.AppOllama;

/// <summary>
/// Chooses the port of the app's own Ollama server: <see cref="AppOllamaDefaults.Port"/> when it is free, otherwise the next ones up
/// (<see cref="AppOllamaDefaults.PortTries"/>), otherwise any free port the system gives. The user's Ollama port
/// (<see cref="AppOllamaDefaults.UserOllamaPort"/>) is never chosen, whatever the system says.
/// </summary>
public static class AppOllamaPorts
{
    /// <summary>
    /// The port to use: <paramref name="isFree"/> answers whether a port can be listened on now, <paramref name="anyFree"/> gives a free port
    /// from the system (asked at most a few times when it keeps naming a port that is not allowed).
    /// </summary>
    public static int Choose(Func<int, bool> isFree, Func<int> anyFree, int preferred = AppOllamaDefaults.Port)
    {
        ArgumentNullException.ThrowIfNull(isFree);
        ArgumentNullException.ThrowIfNull(anyFree);
        for (var i = 0; i < AppOllamaDefaults.PortTries; i++)
        {
            var port = preferred + i;
            if (Allowed(port) && isFree(port))
            {
                return port;
            }
        }

        for (var i = 0; i < 5; i++)
        {
            var port = anyFree();
            if (Allowed(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException("No free port was found for the app's own Ollama server.");
    }

    /// <summary>Whether <paramref name="port"/> may be used (a real port, never the user's Ollama port).</summary>
    public static bool Allowed(int port) => port is > 1024 and <= 65535 && port != AppOllamaDefaults.UserOllamaPort;
}
