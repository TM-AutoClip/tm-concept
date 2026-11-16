using System.Collections.Concurrent;
using TMConcept.Core.Endpoints;

namespace TMConcept.Core.Models;

/// <summary>The models of the catalog entries, made once per "provider/id" from the registered local sources (see <see cref="IModelDirectory"/>).</summary>
public sealed class ModelDirectory(IEnumerable<ILocalModelSource> sources) : IModelDirectory
{
    private readonly IReadOnlyList<ILocalModelSource> _sources = sources.ToList();
    private readonly ConcurrentDictionary<string, IModel> _models = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public IModel Get(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return _models.GetOrAdd(ModelBase.KeyOf(model), _ => Create(model));
    }

    private IModel Create(CatalogModel model)
    {
        foreach (var source in _sources)
        {
            if (source.Create(model) is { } local)
            {
                return local;
            }
        }

        return model.Provider == ModelProviders.Local
            ? new ProcessorModel(model)
            : new OnlineModel(model, model.Provider != ModelProviders.Off && !EndpointCodes.IsEndpoint(model.Provider));
    }
}