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
    public string Name { get; } = name;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = dependsOn;

    /// <inheritdoc />
    public bool Supported => tools.All(t => Installer.Archive(t) is not null || Installer.Status(t).Ready);

    /// <inheritdoc />
    public bool Ready => tools.All(t => Installer.Status(t).Ready);

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() =>
        tools.Where(t => !Installer.Status(t).Ready).Select(Installer.Archive).OfType<ToolArchive>()
            .Select(a => new ComponentDownload(Name + " " + a.Version, a.Url.Host, a.ApproximateBytes)).ToList();

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => tools.Where(t => Installer.Status(t) is { Ready: true, Source: "app" }).Select(Installer.InstallFolder).ToList();

    /// <inheritdoc />
    public virtual Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var missing = tools.Where(t => !Installer.Status(t).Ready).ToList();
        for (var i = 0; i < missing.Count; i++)
        {
            var index = i;
            var inner = new Progress<ToolProgress>(p => progress?.Report(new ToolProgress((index + p.Share) / missing.Count, p.Text)));
            await Installer.InstallAsync(missing[i], agreed: true, inner, cancellationToken).ConfigureAwait(false);
        }
    }
}
