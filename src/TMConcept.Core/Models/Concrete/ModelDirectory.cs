using System.Collections.Concurrent;
using TMConcept.Core.Endpoints;

namespace TMConcept.Core.Models;

/// <summary>
/// The models of the catalog entries, made once per "provider/id" from the registered local sources (see <see cref="IModelDirectory"/>). The
/// models of user-added servers are made again after the saved servers change (<see cref="EndpointRegistry.Changed"/>): a server whose
/// address moved to or from this computer changes whether its models take the graphics card. A server model that may still be loaded is
/// kept, so the graphics card queue can still unload it.
/// </summary>
public sealed class ModelDirectory : IModelDirectory
{
    private readonly IReadOnlyList<ILocalModelSource> _sources;
    private readonly ConcurrentDictionary<string, IModel> _models = new(StringComparer.Ordinal);

    // Server models kept at a change because they were loaded then: made again once they are unloaded.
    private readonly ConcurrentDictionary<string, bool> _stale = new(StringComparer.Ordinal);

    /// <summary>Creates; <paramref name="registry"/> (when given) makes the server models be made again after every change of the saved servers.</summary>
    public ModelDirectory(IEnumerable<ILocalModelSource> sources, EndpointRegistry? registry = null)
    {
        _sources = sources.ToList();
        if (registry is not null)
        {
            registry.Changed += (_, _) => ForgetServerModels();
        }
    }

    /// <inheritdoc />
    public IModel Get(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var key = ModelBase.KeyOf(model);
        if (_stale.ContainsKey(key) && _models.TryGetValue(key, out var kept) && kept is not ILocalModel { IsLoaded: true })
        {
            _models.TryRemove(key, out _);
            _stale.TryRemove(key, out _);
        }

        return _models.GetOrAdd(key, _ => Create(model));
    }

    private void ForgetServerModels()
    {
        foreach (var (key, model) in _models)
        {
            if (!EndpointCodes.IsEndpoint(model.Provider))
            {
                continue;
            }

            if (model is ILocalModel { IsLoaded: true })
            {
                _stale[key] = true;
            }
            else
            {
                _models.TryRemove(key, out _);
            }
        }
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
            : new OnlineModel(model, StepModelResolver.NeedsKey(model.Provider) && model.Provider != ModelProviders.Off);
    }
}
