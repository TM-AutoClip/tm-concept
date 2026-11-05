namespace TMConcept.Core.Models;

/// <summary>Every known model with its prices (embedded catalogs + the user's price overrides).</summary>
public interface IModelCatalog
{
    /// <summary>All models.</summary>
    IReadOnlyList<CatalogModel> All { get; }

    /// <summary>A model; <c>null</c> when unknown.</summary>
    CatalogModel? Find(string provider, string id);

    /// <summary>Models with every flag of <paramref name="capability"/>.</summary>
    IReadOnlyList<CatalogModel> With(ModelCapability capability);

    /// <summary>Replaces the user's price overrides (a price for a missing model adds that model as a chat model).</summary>
    void UseOverrides(IReadOnlyList<ModelPriceOverride> overrides);
}
