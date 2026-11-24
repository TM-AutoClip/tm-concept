using TMConcept.Core.Components;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// The local music model's Python environment as a component (0.1.46, C5; code <see cref="ComponentCodes.MusicEnvironment"/>, after the app's
/// Python): rakkase's and ACE-Step 1.5's sources and ACE-Step's packages (PyTorch for NVIDIA CUDA 12.8; large). Its download lines name the
/// two archives and the packages with their sizes, so the one confirmation shows how large it is.
/// </summary>
public sealed class MusicEnvironmentComponent(RakkaseInstaller installer, RakkaseLayout layout) : IComponent
{
    /// <inheritdoc />
    public string Code => ComponentCodes.MusicEnvironment;

    /// <inheritdoc />
    public string Name => "ACE-Step 1.5 music model (rakkase) Python packages";

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [ComponentCodes.Python];

    /// <inheritdoc />
    public bool Supported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <inheritdoc />
    public bool Ready => installer.EnvironmentReady;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() => Ready
        ? []
        :
        [
            new ComponentDownload("rakkase " + RakkasePins.RakkaseCommit[..7] + " (MIT)", RakkasePins.ArchiveHost, RakkasePins.Rakkase.ApproximateBytes),
            new ComponentDownload("ACE-Step 1.5 " + RakkasePins.AceCommit[..7] + " (MIT)", RakkasePins.ArchiveHost, RakkasePins.Ace.ApproximateBytes),
            new ComponentDownload("ACE-Step 1.5 Python packages (PyTorch for NVIDIA CUDA 12.8; large)", "download.pytorch.org, pypi.org", RakkasePins.PackageBytes),
        ];

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [layout.RakkaseFolder, layout.Environment];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => installer.InstallEnvironmentAsync(progress, cancellationToken);
}
