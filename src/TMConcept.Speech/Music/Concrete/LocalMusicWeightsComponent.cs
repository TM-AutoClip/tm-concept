using TMConcept.Core.Components;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// The weights of one local music model as a component (28 Nov 2026; code <see cref="ComponentCodes.MusicModelPrefix"/> + its catalog id,
/// after its family's environment and source, rule 18: a model is never downloaded by itself). A non-commercial licence is named in the
/// download line, so the install window says it too (the page asked for it before, once per model). Its folders are its own: files it shares
/// with other models of its family stay when it is removed; it names them as shared while its own folder is there (made when its install
/// starts), so they are never a leftover while a model uses them and become one when the family's last model is removed.
/// </summary>
public sealed class LocalMusicWeightsComponent(LocalMusicFamily family, LocalMusicVariant variant, IReadOnlyList<string> dependsOn, string license,
    bool nonCommercial, LocalMusicStore store, LocalMusicLayout layout) : IComponent
{
    /// <inheritdoc />
    public string Code => ComponentCodes.MusicModelPrefix + variant.Id;

    /// <inheritdoc />
    public string Name => family.Name + " " + variant.Id + " weights" + (license.Length == 0 ? string.Empty
        : nonCommercial ? $" ({license}: NON-COMMERCIAL use only)" : $" ({license})");

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = dependsOn;

    /// <inheritdoc />
    public bool Supported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <inheritdoc />
    public bool Ready => store.ModelReady(family, variant);

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads()
    {
        var missing = store.Missing(family, variant);
        return missing.Count == 0 ? [] : [new ComponentDownload(Name, missing[0].Url.Host, missing.Sum(f => f.ApproximateBytes))];
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [layout.ModelFolder(family, variant)];

    /// <inheritdoc />
    public IReadOnlyList<string> SharedFolders() => Directory.Exists(layout.ModelFolder(family, variant))
        ? [.. layout.OutsideFolders(family, variant), layout.Home]
        : [];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => store.InstallModelAsync(family, variant, progress, cancellationToken);
}
