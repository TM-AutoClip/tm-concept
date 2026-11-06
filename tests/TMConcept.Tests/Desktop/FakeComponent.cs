using TMConcept.Core.Components;
using TMConcept.Core.Tools;

namespace TMConcept.Tests.Desktop;

/// <summary>A component that "installs" by setting a flag (or fails); nothing is downloaded.</summary>
internal sealed class FakeComponent(string code, bool ready, params string[] dependsOn) : IComponent
{
    public string Code { get; } = code;

    public string Name => "Fake " + Code;

    public IReadOnlyList<string> DependsOn { get; } = dependsOn;

    public bool Supported { get; set; } = true;

    public bool Ready { get; set; } = ready;

    public bool Fail { get; set; }

    public int Installs { get; private set; }

    public IReadOnlyList<ComponentDownload> Downloads() => Ready ? [] : [new ComponentDownload(Name, "example.org", 1_000_000)];

    public IReadOnlyList<string> Folders() => [];

    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        Installs++;
        if (Fail)
        {
            throw new ToolInstallException("fake failure") { Code = "download" };
        }

        Ready = true;
        return Task.CompletedTask;
    }
}
