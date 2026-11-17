using TMConcept.Core.AppOllama;

namespace TMConcept.Tests.Providers;

/// <summary>The app's own Ollama server as already running at a fake server's address (nothing is started).</summary>
internal sealed class FakeRunningAppServer(string root) : IAppOllamaServer
{
    public int Starts { get; private set; }

    public bool Running => true;

    public Uri? Address { get; } = new(root.TrimEnd('/') + "/");

    public Task<Uri> StartAsync(CancellationToken cancellationToken)
    {
        Starts++;
        return Task.FromResult(Address!);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task EndLeftoversAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
