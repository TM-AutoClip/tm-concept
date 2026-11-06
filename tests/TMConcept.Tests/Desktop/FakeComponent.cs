using TMConcept.Core.Components;
using TMConcept.Core.Tools;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// A component that "installs" by setting a flag (or fails, or is cancelled); nothing is downloaded. <see cref="FoundOnRefresh"/> plays a
/// program found on the system when looked at; <see cref="Folder"/> plays files on disk.
/// </summary>
internal sealed class FakeComponent(string code, bool ready, params string[] dependsOn) : IComponent
{
    public string Code { get; } = code;

    public string Name => "Fake " + Code;

    public IReadOnlyList<string> DependsOn { get; } = dependsOn;

    public bool Supported { get; set; } = true;

    public bool Ready { get; set; } = ready;

    public bool Fail { get; set; }

    public bool Cancel { get; set; }

    public bool FoundOnRefresh { get; set; }

    public string? Folder { get; set; }

    public int Refreshes { get; private set; }

    public int Installs { get; private set; }

    public IReadOnlyList<ComponentDownload> Downloads() => Ready ? [] : [new ComponentDownload(Name, "example.org", 1_000_000)];

    public IReadOnlyList<string> Folders() => Folder is null ? [] : [Folder];

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        Refreshes++;
        Ready |= FoundOnRefresh;
        return Task.CompletedTask;
    }

    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        Installs++;
        if (Cancel)
        {
            throw new OperationCanceledException("fake cancel");
        }

        if (Fail)
        {
            throw new ToolInstallException("fake failure") { Code = "download" };
        }

        Ready = true;
        return Task.CompletedTask;
    }
}
