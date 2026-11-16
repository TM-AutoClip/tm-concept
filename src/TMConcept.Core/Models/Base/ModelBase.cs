namespace TMConcept.Core.Models;

/// <summary>The common part of every <see cref="IModel"/>: everything read from its catalog entry.</summary>
public abstract class ModelBase : IModel
{
    /// <summary>Over a catalog entry.</summary>
    protected ModelBase(CatalogModel catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Catalog = catalog;
    }

    /// <inheritdoc />
    public CatalogModel Catalog { get; }

    /// <inheritdoc />
    public string Provider => Catalog.Provider;

    /// <inheritdoc />
    public string Id => Catalog.Id;

    /// <inheritdoc />
    public string Name => Catalog.Name;

    /// <inheritdoc />
    public ModelCapability Capabilities => Catalog.Capabilities;

    /// <inheritdoc />
    public abstract bool IsLocal { get; }

    /// <inheritdoc />
    public string Key => KeyOf(Catalog);

    /// <summary>"provider/id" of a catalog entry.</summary>
    public static string KeyOf(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider + "/" + model.Id;
    }

    /// <inheritdoc />
    public override string ToString() => Key;
}