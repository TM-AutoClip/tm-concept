namespace TMConcept.Core.AppOllama;

/// <summary>How the app's own Ollama server is started: its program, folder, port and the environment it gets (<see cref="AppOllamaEnvironment"/>).</summary>
/// <param name="Executable">The <c>ollama</c> program of the app's copy.</param>
/// <param name="WorkingDirectory">The folder it starts in (its own install folder).</param>
/// <param name="Port">The port it listens on (127.0.0.1 only; never the user's Ollama port).</param>
/// <param name="Environment">The variables set for it (every inherited <c>OLLAMA_*</c> variable is removed first).</param>
public sealed record AppOllamaLaunch(string Executable, string WorkingDirectory, int Port, IReadOnlyDictionary<string, string> Environment);
