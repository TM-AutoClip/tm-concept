using System.Text.Json;
using TMConcept.Core.Models;

namespace TMConcept.Providers.Catalog;

/// <summary>
/// <see cref="IModelCatalog"/> from the embedded files <c>Catalogs/&lt;provider&gt;.json</c> (one per provider; a new provider = a new file;
/// <c>local.json</c> holds the models that run on this computer). Their prices are the built-in table; newer price tables (the repository's
/// prices file, OpenRouter's list) replace prices of known models. Local models are never repriced. Thread-safe reads; a new set of
/// prices replaces the list in one step.
/// </summary>
public sealed class EmbeddedModelCatalog : IModelCatalog
{
    private const string Prefix = "TMConcept.Catalogs.";
    private readonly IReadOnlyList<CatalogModel> _embedded;
    private readonly PriceTable _builtIn;
    private volatile IReadOnlyList<CatalogModel> _all;
    private volatile IReadOnlyList<PriceTable> _prices;

    /// <summary>Reads the embedded catalogs.</summary>
    public EmbeddedModelCatalog()
    {
        var assembly = typeof(EmbeddedModelCatalog).Assembly;
        var models = new List<CatalogModel>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            var file = JsonSerializer.Deserialize(stream, ProvidersJsonContext.Default.ModelCatalogFile)
                ?? throw new InvalidDataException("Empty catalog file " + name);
            foreach (var model in file.Models)
            {
                model.Provider = file.Provider;
                model.PricedOn = model.PricedOn.Length > 0 ? model.PricedOn : file.PricedOn;
                model.PriceSource = PriceSources.BuiltIn;
                models.Add(model);
            }
        }

        _embedded = models;
        _all = models;
        _builtIn = new PriceTable
        {
            Source = PriceSources.BuiltIn,
            PricedOn = models.Where(m => m.Provider != ModelProviders.Local).Select(m => m.PricedOn).DefaultIfEmpty(string.Empty).Max()!,
        };
        _prices = [_builtIn];
    }

    /// <inheritdoc />
    public IReadOnlyList<CatalogModel> All => _all;

    /// <inheritdoc />
    public IReadOnlyList<PriceTable> Prices => _prices;

    /// <inheritdoc />
    public CatalogModel? Find(string provider, string id) => _all.FirstOrDefault(m => m.Provider == provider && m.Id == id);

    /// <inheritdoc />
    public IReadOnlyList<CatalogModel> With(ModelCapability capability) => _all.Where(m => (m.Capabilities & capability) == capability).ToList();

    /// <inheritdoc />
    public void ApplyPrices(IReadOnlyList<PriceTable> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        var list = _embedded.Select(Copy).ToList();
        foreach (var table in tables)
        {
            foreach (var price in table.Models)
            {
                if (price.Provider == ModelProviders.Local || list.FirstOrDefault(m => m.Provider == price.Provider && m.Id == price.Id) is not { } model)
                {
                    continue;
                }

                model.InputUsd = price.InputUsd ?? model.InputUsd;
                model.OutputUsd = price.OutputUsd ?? model.OutputUsd;
                model.CacheReadUsd = price.CacheReadUsd ?? model.CacheReadUsd;
                model.CacheWriteUsd = price.CacheWriteUsd ?? model.CacheWriteUsd;
                model.LongInputUsd = price.LongInputUsd ?? model.LongInputUsd;
                model.LongOutputUsd = price.LongOutputUsd ?? model.LongOutputUsd;
                model.ImageUsd = price.ImageUsd ?? model.ImageUsd;
                model.SpeechUsdPerMillionCharacters = price.SpeechUsdPerMillionCharacters ?? model.SpeechUsdPerMillionCharacters;
                model.PricedOn = table.PricedOn;
                model.PriceSource = table.Source;
            }
        }

        _all = list;
        _prices = [_builtIn, .. tables];
    }

    private static CatalogModel Copy(CatalogModel m) => new()
    {
        Provider = m.Provider,
        Id = m.Id,
        Name = m.Name,
        Capabilities = m.Capabilities,
        InputUsd = m.InputUsd,
        OutputUsd = m.OutputUsd,
        CacheReadUsd = m.CacheReadUsd,
        CacheWriteUsd = m.CacheWriteUsd,
        LongContextTokens = m.LongContextTokens,
        LongInputUsd = m.LongInputUsd,
        LongOutputUsd = m.LongOutputUsd,
        ImageUsd = m.ImageUsd,
        SpeechUsdPerMillionCharacters = m.SpeechUsdPerMillionCharacters,
        ContextTokens = m.ContextTokens,
        MaxOutputTokens = m.MaxOutputTokens,
        Efforts = [.. m.Efforts],
        PricedOn = m.PricedOn,
        RetiresOn = m.RetiresOn,
        PriceSource = m.PriceSource,
        Requires = [.. m.Requires],
        MinVideoMemoryGb = m.MinVideoMemoryGb,
    };
}
