using TMConcept.Core.AppOllama;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Models;

namespace TMConcept.Providers.Catalog;

/// <summary>
/// The app's <see cref="IModelCatalog"/>: the embedded catalogs (<see cref="EmbeddedModelCatalog"/>, with their price updates) plus the
/// models of the user's local servers (<see cref="EndpointRegistry"/>: free, chat, no key). The endpoint part is rebuilt in one step whenever
/// the saved servers change, so a picker that loads after a change offers the new models, and a step choice of a deleted server's model is
/// not found (shown as no longer offered). The models of the app's own Ollama copy (<see cref="AppModelRegistry.Offered"/>, provider
/// <see cref="ModelProviders.AppOllama"/>: the installed ones and the catalogue's others, installed when a run starts) are added the same
/// way and follow their registry's changes. Prices and built-in prices concern
/// only the embedded models.
/// </summary>
public sealed class MergedModelCatalog : IModelCatalog
{
    private readonly EmbeddedModelCatalog _embedded;
    private readonly EndpointRegistry _endpoints;
    private readonly AppModelRegistry? _appModels;
    private volatile IReadOnlyList<CatalogModel> _local = [];

    /// <summary>
    /// Merges <paramref name="embedded"/> with the models of <paramref name="endpoints"/> and of <paramref name="appModels"/> (when given) and
    /// follows their changes.
    /// </summary>
    public MergedModelCatalog(EmbeddedModelCatalog embedded, EndpointRegistry endpoints, AppModelRegistry? appModels = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        _embedded = embedded;
        _endpoints = endpoints;
        _appModels = appModels;
        Rebuild();
        endpoints.Changed += (_, _) => Rebuild();
        if (appModels is not null)
        {
            appModels.Changed += (_, _) => Rebuild();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<CatalogModel> All => [.. _embedded.All, .. _local];

    /// <inheritdoc />
    public IReadOnlyList<PriceTable> Prices => _embedded.Prices;

    /// <inheritdoc />
    public CatalogModel? Find(string provider, string id) =>
        EndpointCodes.IsEndpoint(provider) || provider == ModelProviders.AppOllama ? _local.FirstOrDefault(m => m.Provider == provider && m.Id == id) : _embedded.Find(provider, id);

    /// <inheritdoc />
    public CatalogModel? BuiltIn(string provider, string id) => _embedded.BuiltIn(provider, id);

    /// <inheritdoc />
    public IReadOnlyList<CatalogModel> With(ModelCapability capability) =>
        [.. _embedded.With(capability), .. _local.Where(m => (m.Capabilities & capability) == capability)];

    /// <inheritdoc />
    public void ApplyPrices(IReadOnlyList<PriceTable> tables) => _embedded.ApplyPrices(tables);

    private void Rebuild() => _local = [.. _appModels?.Offered ?? [], .. _endpoints.All.SelectMany(EndpointCodes.Catalog)];
}
