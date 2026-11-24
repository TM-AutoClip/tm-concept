using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;

namespace TMConcept.Core.Music;

/// <summary>
/// <see cref="IMusicGenerators"/> over the catalog, the registered generators and the step resolver (an online model's key the same way as a
/// step's: the newest working one of its provider).
/// </summary>
public sealed class MusicGenerators(IModelCatalog catalog, IEnumerable<IMusicGenerator> generators, StepModelResolver resolver) : IMusicGenerators
{
    private readonly IReadOnlyList<IMusicGenerator> _generators = generators.ToList();

    /// <inheritdoc />
    public IReadOnlyList<CatalogModel> Models => catalog.All.Where(m => m.Capabilities.HasFlag(ModelCapability.Music))
        .OrderBy(m => m.Provider == ModelProviders.Local ? 0 : 1).ToList();

    /// <inheritdoc />
    public CatalogModel? Find(string generator)
    {
        if (string.IsNullOrWhiteSpace(generator) || generator.Split('/', 2) is not [{ } provider, { } id])
        {
            return null;
        }

        return catalog.Find(provider, id) is { } model && model.Capabilities.HasFlag(ModelCapability.Music) ? model : null;
    }

    /// <inheritdoc />
    public bool Ready(CatalogModel model) => For(model)?.Ready(model) ?? false;

    /// <inheritdoc />
    public Task<ResolvedModel> ResolveAsync(CatalogModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        return resolver.ResolveAsync(PipelineSteps.Music, new StepModelChoice { Step = PipelineSteps.Music, Provider = model.Provider, Model = model.Id },
            cancellationToken);
    }

    /// <inheritdoc />
    public IMusicGenerator? For(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return _generators.FirstOrDefault(g => g.Handles(model));
    }
}
