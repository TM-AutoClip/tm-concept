using TMConcept.Core.Components;
using TMConcept.Core.Tools;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Components;

/// <summary>
/// The packages of one Python environment, PyTorch included (after the Python): the CUDA build of PyTorch on a computer with an NVIDIA card
/// (about 4 GB), the processor build otherwise (under 1 GB). Its download line says how large it is and which build it is.
/// </summary>
public sealed class PythonEnvironmentComponent(PythonEnvironment environment, PythonInstaller installer, PythonLayout layout) : IComponent
{
    /// <summary>The component code of an environment.</summary>
    public static string CodeOf(string key) => ComponentCodes.PythonEnvironmentPrefix + key;

    /// <inheritdoc />
    public string Code => CodeOf(environment.Key);

    /// <inheritdoc />
    public string Name => environment.Name + " Python packages";

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [ComponentCodes.Python];

    /// <inheritdoc />
    public bool Supported => true;

    /// <inheritdoc />
    public bool Ready => installer.EnvironmentReady(environment);

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads()
    {
        if (Ready)
        {
            return [];
        }

        var cuda = installer.Backend(environment) == PythonPins.CudaBackend;
        return [new ComponentDownload(Name + (cuda ? " (PyTorch for NVIDIA CUDA 12.8; large)" : " (PyTorch for the processor)"), "download.pytorch.org, pypi.org",
            cuda ? environment.CudaBytes : environment.CpuBytes)];
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [layout.EnvironmentFolder(environment.Key)];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) =>
        installer.InstallEnvironmentAsync(environment, progress, cancellationToken);
}
