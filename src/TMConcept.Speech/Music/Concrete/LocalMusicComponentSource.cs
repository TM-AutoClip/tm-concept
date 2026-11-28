using TMConcept.Core.Components;
using TMConcept.Core.Models;
using TMConcept.Speech.Components;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Music;

/// <summary>
/// The components of the local music families (28 Nov 2026): per family its Python environment (<see cref="PythonEnvironmentComponent"/>,
/// "speech-env.musicgen"; supported only with a usable NVIDIA card), its source when it runs from one ("music-code.audiocraft"), and one
/// weights component per catalog model ("music.musicgen-small") with the licence of its catalog entry.
/// </summary>
public sealed class LocalMusicComponentSource(IModelCatalog catalog, PythonInstaller installer, PythonLayout pythonLayout, LocalMusicStore store,
    LocalMusicLayout layout) : IComponentSource
{
    private readonly Lazy<IReadOnlyList<IComponent>> _components = new(() => LocalMusicFamilies.All.SelectMany((family, index) =>
    {
        var environment = new PythonEnvironmentComponent(family.Environment, installer, pythonLayout);
        var source = family.Source is { } archive ? new LocalMusicSourceComponent(family, SourceName(family, archive), store, layout) : null;
        IReadOnlyList<string> needs = source is null ? [environment.Code] : [environment.Code, source.Code];
        var weights = family.Variants.Select(variant => catalog.Find(ModelProviders.Local, variant.Id) is { } model
            ? new LocalMusicWeightsComponent(family, variant, needs, model.License.ToUpperInvariant(), model.LicenseUse == LicenseUse.NonCommercial, store, layout)
            : new LocalMusicWeightsComponent(family, variant, needs, string.Empty, false, store, layout));
        // Two families may share an environment (DiffRhythm 1.0 and 1.2): it is one component, listed with the first of them.
        var components = new List<IComponent>();
        if (LocalMusicFamilies.All.Take(index).All(f => f.Environment.Key != family.Environment.Key))
        {
            components.Add(environment);
        }

        if (source is not null)
        {
            components.Add(source);
        }

        components.AddRange(weights);
        return components;
    }).ToList());

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => _components.Value;

    /// <summary>
    /// The name of a family's source component: the family's own (<see cref="LocalMusicFamily.SourceName"/>) or its archive's root folder's
    /// name before the commit ("audiocraft").
    /// </summary>
    public static string SourceName(LocalMusicFamily family, Core.Tools.ToolArchive archive)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(archive);
        if (family.SourceName.Length > 0)
        {
            return family.SourceName;
        }

        var dash = archive.Root.LastIndexOf('-');
        return dash > 0 ? archive.Root[..dash].ToLowerInvariant() : archive.Root.ToLowerInvariant();
    }
}
