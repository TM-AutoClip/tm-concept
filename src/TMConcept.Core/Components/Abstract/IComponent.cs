using TMConcept.Core.Tools;

namespace TMConcept.Core.Components;

/// <summary>
/// Something a chosen setting needs on this computer: a program (Node.js, ffmpeg, stable-diffusion.cpp), libraries (sherpa-onnx), engine
/// packages with their browser, a voice model or image model weights. Installed only after the user confirmed the list of downloads
/// (when a setting is saved, or before the first video). Components come from DI (<see cref="IComponentSource"/>).
/// </summary>
public interface IComponent
{
    /// <summary>Code (<see cref="ComponentCodes"/>).</summary>
    string Code { get; }

    /// <summary>Display name.</summary>
    string Name { get; }

    /// <summary>Components that must be ready first (installed before this one when it is missing).</summary>
    IReadOnlyList<string> DependsOn { get; }

    /// <summary>Whether this computer has a download for it (no stable-diffusion.cpp build for Linux ARM64, for example).</summary>
    bool Supported { get; }

    /// <summary>Whether it is ready (files only; no process, no network).</summary>
    bool Ready { get; }

    /// <summary>What an install would download now (empty when ready).</summary>
    IReadOnlyList<ComponentDownload> Downloads();

    /// <summary>The folders it occupies (its size and what Remove takes away); empty when it is the system's copy.</summary>
    IReadOnlyList<string> Folders();

    /// <summary>Looks again at what is on the computer (a suitable Node.js on the PATH); may start a short process. No network.</summary>
    Task RefreshAsync(CancellationToken cancellationToken);

    /// <summary>Installs (network; called only after the user confirmed the downloads).</summary>
    Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken);
}
