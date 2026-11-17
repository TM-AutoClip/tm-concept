using TMConcept.Core.AppOllama;

namespace TMConcept.Tests.Core;

/// <summary>Records every start instead of running a program; ports are free unless a test says otherwise.</summary>
internal sealed class FakeAppOllamaLauncher : IAppOllamaLauncher
{
    public HashSet<int> Taken { get; } = [];

    public int Free { get; set; } = 40_123;

    /// <summary>What a started process does at once (a test may end it to simulate a crash at start).</summary>
    public Action<FakeAppOllamaProcess>? OnStart { get; set; }

    public List<FakeAppOllamaProcess> Started { get; } = [];

    public bool IsPortFree(int port) => !Taken.Contains(port);

    public int AnyFreePort() => Free;

    public IAppOllamaProcess Start(AppOllamaLaunch launch)
    {
        var process = new FakeAppOllamaProcess(launch);
        Started.Add(process);
        OnStart?.Invoke(process);
        return process;
    }
}
