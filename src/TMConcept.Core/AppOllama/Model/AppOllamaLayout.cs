namespace TMConcept.Core.AppOllama;

/// <summary>
/// Where the app's own Ollama copy keeps its things, all inside the app's data folder and apart from an Ollama the user installed: the models
/// (<c>OLLAMA_MODELS</c>) and the home folder its server sees (its keys and settings, normally in the user's <c>~/.ollama</c>).
/// </summary>
/// <param name="ModelsDirectory">The models folder (<c>&lt;tools&gt;/ollama-models</c>): <c>manifests/</c> and <c>blobs/</c>.</param>
/// <param name="HomeDirectory">The home folder of its server (<c>&lt;tools&gt;/ollama-home</c>).</param>
public sealed record AppOllamaLayout(string ModelsDirectory, string HomeDirectory);
