using System.Text.Json;
using TMConcept.Core.Models;

namespace TMConcept.Providers.Catalog;

/// <summary>
/// <see cref="IModelCatalog"/> from the embedded files <c>Catalogs/&lt;provider&gt;.json</c> (one per provider; a new provider = a new file) plus
/// the user's price overrides. Thread-safe reads; overrides replace the list in one step.
/// </summary>
public sealed class EmbeddedModelCatalog : IModelCatalog
{
    private const string Prefix = "TMConcept.Catalogs.";
    private readonly IReadOnlyList<CatalogModel> _embedded;
    private volatile IReadOnlyList<CatalogModel> _all;

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
                models.Add(model);
            }
        }

        _embedded = models;
        _all = models;
    }

    /// <inheritdoc />
    public IReadOnlyList<CatalogModel> All => _all;

    /// <inheritdoc />
    public CatalogModel? Find(string provider, string id) => _all.FirstOrDefault(m => m.Provider == provider && m.Id == id);

    /// <inheritdoc />
    public IReadOnlyList<CatalogModel> With(ModelCapability capability) => _all.Where(m => (m.Capabilities & capability) == capability).ToList();

    /// <inheritdoc />
    public void UseOverrides(IReadOnlyList<ModelPriceOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var list = _embedded.Select(Copy).ToList();
        foreach (var price in overrides)
        {
            var model = list.FirstOrDefault(m => m.Provider == price.Provider && m.Id == price.ModelId);
            if (model is null)
            {
                model = new CatalogModel { Provider = price.Provider, Id = price.ModelId, Name = price.ModelId, Capabilities = ModelCapability.Chat };
                list.Add(model);
            }

            model.InputUsd = price.InputUsd;
            model.OutputUsd = price.OutputUsd;
            model.CacheReadUsd = price.CacheReadUsd;
            model.UserPrice = true;
            model.PricedOn = price.UpdatedAtUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        _all = list;
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
    };
}
