using TMConcept.Core.AppOllama;

namespace TMConcept.Tests.Core;

/// <summary>A pretend <c>ollama serve</c>: no process at all; a test ends it or reads whether it was stopped.</summary>
internal sealed class FakeAppOllamaProcess(AppOllamaLaunch launch) : IAppOllamaProcess
{
    public AppOllamaLaunch Launch { get; } = launch;

    public bool HasExited { get; set; }

    public int? ExitCode { get; set; }

    public string RecentOutput { get; set; } = string.Empty;

    public int Stops { get; private set; }

    public bool Disposed { get; private set; }

    public void Stop()
    {
        Stops++;
        HasExited = true;
    }

    public void Dispose() => Disposed = true;
}
