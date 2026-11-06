namespace TMConcept.Core.Models;

/// <summary>Every known model with its prices: the embedded catalogs, with prices updated from the newer price tables.</summary>
public interface IModelCatalog
{
    /// <summary>All models.</summary>
    IReadOnlyList<CatalogModel> All { get; }

    /// <summary>The price tables in use, oldest source first (the built-in table is always the first).</summary>
    IReadOnlyList<PriceTable> Prices { get; }

    /// <summary>A model; <c>null</c> when unknown.</summary>
    CatalogModel? Find(string provider, string id);

    /// <summary>Models with every flag of <paramref name="capability"/>.</summary>
    IReadOnlyList<CatalogModel> With(ModelCapability capability);

    /// <summary>Applies price tables over the built-in prices, in order (a later table wins); unknown models are ignored.</summary>
    void ApplyPrices(IReadOnlyList<PriceTable> tables);
}
