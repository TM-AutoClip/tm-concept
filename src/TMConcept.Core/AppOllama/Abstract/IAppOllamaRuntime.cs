namespace TMConcept.Core.AppOllama;

/// <summary>
/// The installed program of the app's own Ollama copy (the "Ollama (TM Concept)" component): whether it is ready and where its
/// <c>ollama</c> program is. Files only; nothing is started or downloaded here.
/// </summary>
public interface IAppOllamaRuntime
{
    /// <summary>Whether this computer has a download of it (Windows x64, Linux x64 and ARM64).</summary>
    bool Supported { get; }

    /// <summary>Whether the pinned version is installed and finished.</summary>
    bool Ready { get; }

    /// <summary>The pinned Ollama version (<c>0.40.16</c>).</summary>
    string Version { get; }

    /// <summary>The <c>ollama</c> program of the installed copy (only meaningful when <see cref="Ready"/>).</summary>
    string Executable { get; }
}
