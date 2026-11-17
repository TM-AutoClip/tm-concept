using System.Globalization;

namespace TMConcept.Core.AppOllama;

/// <summary>
/// The environment of the app's own Ollama server. Every <c>OLLAMA_*</c> variable the app inherited is removed first (an Ollama the user set
/// up through variables, <c>OLLAMA_HOST</c> or <c>OLLAMA_MODELS</c> above all, must never steer the app's copy into the user's server or
/// models), then: <c>OLLAMA_HOST</c> = 127.0.0.1 with the app's own port (never reachable from another computer), <c>OLLAMA_MODELS</c> = the
/// app's own models folder, one model in memory and one request at a time (<c>OLLAMA_MAX_LOADED_MODELS=1</c>,
/// <c>OLLAMA_NUM_PARALLEL=1</c>: the same rule as the app's graphics card coordinator), no cloud models (<c>OLLAMA_NO_CLOUD=1</c>), and a
/// home folder of its own (<c>HOME</c>, <c>USERPROFILE</c>) so its keys and settings never mix with the user's <c>~/.ollama</c>.
/// </summary>
public static class AppOllamaEnvironment
{
    /// <summary>The prefix of every variable Ollama reads.</summary>
    public const string Prefix = "OLLAMA_";

    /// <summary>The variables the app's server gets for <paramref name="port"/>.</summary>
    public static IReadOnlyDictionary<string, string> For(AppOllamaLayout layout, int port)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (port is <= 0 or > 65535 || port == AppOllamaDefaults.UserOllamaPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "The app's Ollama never listens on the user's Ollama port.");
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OLLAMA_HOST"] = AppOllamaDefaults.Host + ":" + port.ToString(CultureInfo.InvariantCulture),
            ["OLLAMA_MODELS"] = layout.ModelsDirectory,
            ["OLLAMA_MAX_LOADED_MODELS"] = "1",
            ["OLLAMA_NUM_PARALLEL"] = "1",
            ["OLLAMA_KEEP_ALIVE"] = AppOllamaDefaults.KeepAlive,
            ["OLLAMA_NO_CLOUD"] = "1",
            ["HOME"] = layout.HomeDirectory,
            ["USERPROFILE"] = layout.HomeDirectory,
        };
    }

    /// <summary>
    /// Applies <paramref name="variables"/> to a process environment: every inherited <c>OLLAMA_*</c> variable is removed (any case), then
    /// each one of <paramref name="variables"/> is set.
    /// </summary>
    public static void Apply(IDictionary<string, string?> environment, IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(variables);
        foreach (var name in environment.Keys.Where(k => k.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            environment.Remove(name);
        }

        foreach (var (name, value) in variables)
        {
            environment[name] = value;
        }
    }

    /// <summary>The root address of a server on <paramref name="port"/> (<c>http://127.0.0.1:11534/</c>).</summary>
    public static Uri Root(int port) => new("http://" + AppOllamaDefaults.Host + ":" + port.ToString(CultureInfo.InvariantCulture) + "/");
}
