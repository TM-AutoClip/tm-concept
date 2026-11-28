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

    /// <summary>
    /// The folders of files it uses together with other components (28 Nov 2026: a local music family's shared model files, such as MAGNeT's
    /// T5 or DiffRhythm's VAE, and the folder its libraries write to): never taken by its Remove (another component still needs them) and never
    /// a leftover while a component names them. Empty by default.
    /// </summary>
    IReadOnlyList<string> SharedFolders() => [];

    /// <summary>
    /// The folders and files an install of it replaces (28 Nov 2026, the developer: "sıfırdan kur ve güncellerde modelleri silecek önce, kalıcı
    /// tekte silecek, geri dönüşüm değil, sonra indirecek"): deleted for good right before its download starts, so an update never leaves the
    /// old copy next to the new one and an unfinished install never resumes on half-written files. What another component names as its own or
    /// shared folder stays (<see cref="ComponentPlanner"/>). By default <see cref="Folders"/>; a component whose new version goes into a new
    /// folder names the folder of every version (and its download in the shared downloads folder), and one that is installed by another
    /// component (the engines' browser) names nothing. The planner deletes nothing while <see cref="Downloads"/> is empty: a component that is
    /// not ready only because a dependency is missing keeps its files (its install downloads none of them again).
    /// </summary>
    IReadOnlyList<string> ReplacedPaths() => Folders();

    /// <summary>
    /// Whether an install of this app left it on disk, ready or not, in this version or an earlier one (a new app version may pin a newer
    /// download in a new folder): an update after a new app version installs again exactly these (with what <see cref="IInstallRecord"/>
    /// says). By default: one of its folders holds a finished file (<see cref="ComponentFolders.HasContent"/>; an empty folder or one with
    /// only a partial download does not count).
    /// </summary>
    bool WasInstalled => Folders().Any(ComponentFolders.HasContent);

    /// <summary>
    /// Whether this app process has loaded a library from its folders (0.1.27: PDFium, the OCR's DirectML runtime; they are never unloaded
    /// while the app runs): its folder cannot be moved or deleted until the app starts again. False by default.
    /// </summary>
    bool LoadedInProcess => false;

    /// <summary>Looks again at what is on the computer (a suitable Node.js on the PATH); may start a short process. No network.</summary>
    Task RefreshAsync(CancellationToken cancellationToken);

    /// <summary>Installs (network; called only after the user confirmed the downloads).</summary>
    Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken);
}
