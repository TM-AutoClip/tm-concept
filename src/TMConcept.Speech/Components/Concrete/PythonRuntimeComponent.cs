using TMConcept.Core.Components;
using TMConcept.Core.Tools;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Components;

/// <summary>The Python of the Python voices (uv installs it into the tools folder; after uv).</summary>
public sealed class PythonRuntimeComponent(PythonInstaller installer, PythonLayout layout) : IComponent
{
    /// <inheritdoc />
    public string Code => ComponentCodes.Python;

    /// <inheritdoc />
    public string Name => "Python " + PythonPins.PythonVersion;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [ComponentCodes.Uv];

    /// <inheritdoc />
    public bool Supported => true;

    /// <inheritdoc />
    public bool Ready => installer.PythonReady;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() =>
        Ready ? [] : [new ComponentDownload(Name + " (python-build-standalone, by uv)", "github.com", PythonPins.PythonBytes)];

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [layout.PythonFolder, layout.CacheFolder];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => installer.InstallPythonAsync(progress, cancellationToken);
}
