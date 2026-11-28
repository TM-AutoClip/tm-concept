using TMConcept.Core.Endpoints;

namespace TMConcept.Core.Models;

/// <inheritdoc cref="IModelListOrder" />
public sealed class ModelListOrder : IModelListOrder
{
    /// <inheritdoc />
    public ModelListGroup GroupOf(CatalogModel model, bool cannotRun)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (cannotRun)
        {
            return ModelListGroup.CannotRun;
        }

        // A model of this computer: one the app installs (local), the app's own Ollama copy, a user-added server (it may be on another
        // computer of the user, but it is the user's own server, not a paid API: it belongs with the local ones, as in the step pickers before).
        return model.Provider == ModelProviders.Local || model.Provider == ModelProviders.AppOllama || EndpointCodes.IsEndpoint(model.Provider)
            ? ModelListGroup.Runs
            : ModelListGroup.Online;
    }

    /// <inheritdoc />
    public IReadOnlyList<T> Order<T>(IEnumerable<T> items, Func<T, ModelListGroup> group)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(group);

        // OrderBy is stable: the catalog's (the caller's) order stays inside each group.
        return items.OrderBy(group).ToList();
    }

    /// <inheritdoc />
    public T? First<T>(IEnumerable<T> items, Func<T, ModelListGroup> group)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(group);
        var list = items as IReadOnlyList<T> ?? items.ToList();
        return list.FirstOrDefault(i => group(i) != ModelListGroup.CannotRun) ?? list.FirstOrDefault();
    }
}
