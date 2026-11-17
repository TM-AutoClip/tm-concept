using TMConcept.Core.AppOllama;

namespace TMConcept.Tests.Core;

/// <summary>The app's Ollama program as a test says it is (nothing on disk is needed).</summary>
internal sealed class FakeAppOllamaRuntime(bool ready = true) : IAppOllamaRuntime
{
    public bool Supported { get; set; } = true;

    public bool Ready { get; set; } = ready;

    public string Version => "0.40.16";

    public string Executable { get; set; } = Path.Combine(Path.GetTempPath(), "tm-concept-fake-ollama", "ollama.exe");
}
