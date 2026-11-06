using TMConcept.Core.Components;
using TMConcept.Core.Tools;
using TMConcept.Engines.Engines;

namespace TMConcept.Engines.Components;

/// <summary>
/// The shared Chrome Headless Shell of HyperFrames and Revideo (<c>&lt;tools&gt;/chrome</c>). It is installed with an engine; it is a
/// component of its own only so the installed components list shows its size and can remove it.
/// </summary>
public sealed class BrowserComponent(ChromeLocator chrome) : IComponent
{
    /// <summary>Code.</summary>
    public const string BrowserCode = "browser";

    /// <inheritdoc />
    public string Code => BrowserCode;

    /// <inheritdoc />
    public string Name => "Chrome Headless Shell";

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public bool Supported => true;

    /// <inheritdoc />
    public bool Ready => Directory.Exists(chrome.Root) && chrome.Find() is not null;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() => [];

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [chrome.Root];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;
}
