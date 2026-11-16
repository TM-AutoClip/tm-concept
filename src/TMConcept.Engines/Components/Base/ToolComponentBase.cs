using TMConcept.Core.Components;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Components;

/// <summary>A component made of pinned tool downloads (<see cref="ToolManifest"/>), installed by the <see cref="ToolInstaller"/>.</summary>
public abstract class ToolComponentBase(ToolInstaller installer, string code, string name, IReadOnlyList<ToolKind> tools, IReadOnlyList<string> dependsOn)
    : IComponent
{
    /// <summary>The installer.</summary>
    protected ToolInstaller Installer { get; } = installer;

    /// <inheritdoc />
    public string Code { get; } = code;

    /// <inheritdoc />
    public virtual string Name => name;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = dependsOn;

    /// <inheritdoc />
    public bool Supported => Tools.All(t => Installer.Archive(t) is not null || Installer.Status(t).Ready);

    /// <inheritdoc />
    public bool Ready => Tools.All(t => Installer.Status(t).Ready);

    /// <summary>The tools it is made of now (a subclass may choose them on each call: the image build of the current setting).</summary>
    protected virtual IReadOnlyList<ToolKind> Tools => tools;

    /// <summary>The tools whose folders it shows and removes (by default <see cref="Tools"/>).</summary>
    protected virtual IReadOnlyList<ToolKind> FolderTools => Tools;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads()
    {
        var current = Tools;
        return current.Where(t => !Installer.Status(t).Ready).Select(Installer.Archive).OfType<ToolArchive>()
            .Select(a => new ComponentDownload(current.Count > 1 ? Name + " " + a.Version + " · " + a.Url.Segments[^1] : Name + " " + a.Version, a.Url.Host,
                a.ApproximateBytes)).ToList();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The app's install folder of every tool that is not the system's copy, when it is on disk, ready or not: a half-finished install or
    /// a pinned version that is not used any more still takes space and can be removed.
    /// </remarks>
    public IReadOnlyList<string> Folders() => FolderTools.Where(t => Installer.Status(t) is not { Ready: true, Source: "system" })
        .Select(Installer.InstallFolder).Where(Directory.Exists).Distinct(StringComparer.Ordinal).ToList();

    /// <inheritdoc />
    /// <remarks>
    /// A tool's own folder (<c>tools/&lt;name&gt;</c>), whatever version is inside: a new app version pins a new version in a new folder, so
    /// <see cref="Folders"/> alone would say an updated component was never installed. Every tool of <see cref="FolderTools"/> counts: the
    /// image runtime chooses its build at call time (its constructor list is empty), and a 0.1.15 CUDA install in <c>tools/sd-cpp</c> must
    /// still make the update plan the build chosen now (Vulkan on an AMD card).
    /// </remarks>
    public bool WasInstalled => FolderTools.Any(t => Directory.Exists(Installer.ToolRoot(t)));

    /// <inheritdoc />
    public virtual Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var missing = Tools.Where(t => !Installer.Status(t).Ready).ToList();
        for (var i = 0; i < missing.Count; i++)
        {
            var index = i;
            var inner = new Progress<ToolProgress>(p => progress?.Report(new ToolProgress((index + p.Share) / missing.Count, p.Text)));
            await Installer.InstallAsync(missing[i], agreed: true, inner, cancellationToken).ConfigureAwait(false);
        }
    }
}
