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

    private bool _ready = ready;

    /// <summary>Ready only when its own files are there and, when set, this dependency is ready (an image model after its runtime).</summary>
    public FakeComponent? ReadyOnlyWith { get; set; }

    public bool Ready
    {
        get => _ready && (ReadyOnlyWith?.Ready ?? true);
        set => _ready = value;
    }

    public bool Fail { get; set; }

    public bool Cancel { get; set; }

    public bool FoundOnRefresh { get; set; }

    public string? Folder { get; set; }

    public bool LoadedInProcess { get; set; }

    public int Refreshes { get; private set; }

    public int Installs { get; private set; }

    /// <summary>Runs inside the install, before it ends (what the test looks at meanwhile).</summary>
    public Action? OnInstall { get; set; }

    /// <summary>Its own files only: nothing when they are there, also while it waits for <see cref="ReadyOnlyWith"/>.</summary>
    public IReadOnlyList<ComponentDownload> Downloads() => _ready ? [] : [new ComponentDownload(Name, "example.org", 1_000_000)];

    public IReadOnlyList<string> Folders() => Folder is null ? [] : [Folder];

    /// <summary>What its install replaces (null = its folders, the default of every component).</summary>
    public IReadOnlyList<string>? Replaced { get; set; }

    public IReadOnlyList<string> ReplacedPaths() => Replaced ?? Folders();

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        Refreshes++;
        _ready |= FoundOnRefresh;
        return Task.CompletedTask;
    }

    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        Installs++;
        OnInstall?.Invoke();
        if (Cancel)
        {
            throw new OperationCanceledException("fake cancel");
        }

        if (Fail)
        {
            throw new ToolInstallException("fake failure") { Code = "download" };
        }

        _ready = true;
        return Task.CompletedTask;
    }
}
