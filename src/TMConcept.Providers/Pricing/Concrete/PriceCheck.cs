using System.Globalization;
using System.Text.Json;
using TMConcept.Core.Models;
using TMConcept.Providers.Catalog;

namespace TMConcept.Providers.Pricing;

/// <summary>
/// Reads and checks downloaded prices (untrusted input): the repository's prices file (<see cref="ReadFile"/>) and OpenRouter's models list
/// (<see cref="ReadOpenRouter"/>). Only known models keep an entry; every value must be in <see cref="PriceLimits"/> and near the built-in
/// price (the app's own table). A file that cannot be read, has another schema, a bad or too old date or no models list is refused as a
/// whole (the last good copy stays in use).
/// </summary>
public static class PriceCheck
{
    /// <summary>The repository's file as a checked table; null (with <paramref name="problem"/>) when refused.</summary>
    public static PriceTable? ReadFile(string json, IModelCatalog builtIn, DateTimeOffset now, out string problem, out int dropped)
    {
        ArgumentNullException.ThrowIfNull(builtIn);
        dropped = 0;
        PriceTable? table;
        try
        {
            table = JsonSerializer.Deserialize(json, ProvidersJsonContext.Default.PriceTable);
        }
        catch (JsonException ex)
        {
            problem = "not valid JSON (" + ex.Message + ")";
            return null;
        }

        if (table is null)
        {
            problem = "empty";
            return null;
        }

        return Check(table, PriceSources.Repository, builtIn, now, out problem, out dropped);
    }

    /// <summary>
    /// The checks of a table (downloaded, or the last good copy read again at start): the schema, the date (not in the future, not older
    /// than the app's built-in table: an app newer than the copy would get old prices back), a models list, and every entry
    /// (<see cref="Sane"/>; a broken entry is dropped). Null (with <paramref name="problem"/>) when the table is refused as a whole.
    /// </summary>
    public static PriceTable? Check(PriceTable table, string source, IModelCatalog builtIn, DateTimeOffset now, out string problem, out int dropped)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(builtIn);
        dropped = 0;
        if (table.Schema != PriceTable.CurrentSchema)
        {
            problem = string.Create(CultureInfo.InvariantCulture, $"schema {table.Schema}, this app reads {PriceTable.CurrentSchema}");
            return null;
        }

        if (!DateOnly.TryParseExact(table.PricedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || date > DateOnly.FromDateTime(now.UtcDateTime).AddDays(PriceLimits.FutureDays))
        {
            problem = "bad date '" + Short(table.PricedOn) + "'";
            return null;
        }

        if (string.CompareOrdinal(table.PricedOn, builtIn.Prices[0].PricedOn) < 0)
        {
            problem = "older than the built-in table (" + table.PricedOn + ")";
            return null;
        }

        if (table.Models is null)
        {
            problem = "no models list";
            return null;
        }

        var kept = new List<ModelPrice>();
        foreach (var price in table.Models)
        {
            if (price is not null && Sane(price, builtIn))
            {
                kept.Add(price);
            }
            else
            {
                dropped++;
            }
        }

        problem = string.Empty;
        return new PriceTable { Schema = table.Schema, Source = source, PricedOn = table.PricedOn, Models = kept };
    }

    /// <summary>
    /// OpenRouter's public models list (<c>GET /api/v1/models</c>, no key): <c>data[].id</c> and <c>pricing</c> in USD per token as text
    /// (<c>prompt</c>, <c>completion</c>, <c>input_cache_read</c>, <c>input_cache_write</c>). Only the catalog's OpenRouter models are kept;
    /// a negative price (variable pricing) is skipped. Null (with <paramref name="problem"/>) when the answer cannot be read.
    /// </summary>
    public static PriceTable? ReadOpenRouter(string json, IModelCatalog builtIn, DateTimeOffset now, out string problem, out int dropped)
    {
        ArgumentNullException.ThrowIfNull(builtIn);
        dropped = 0;
        var known = builtIn.All.Where(m => m.Provider == "openrouter").Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var kept = new List<ModelPrice>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                problem = "no data list";
                return null;
            }

            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.String
                    || idValue.GetString() is not { } id || !known.Contains(id) || !item.TryGetProperty("pricing", out var pricing)
                    || pricing.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var price = new ModelPrice
                {
                    Provider = "openrouter",
                    Id = id,
                    InputUsd = PerMillion(pricing, "prompt"),
                    OutputUsd = PerMillion(pricing, "completion"),
                    CacheReadUsd = PerMillion(pricing, "input_cache_read"),
                    CacheWriteUsd = PerMillion(pricing, "input_cache_write"),
                };
                if (Sane(price, builtIn))
                {
                    kept.Add(price);
                }
                else
                {
                    dropped++;
                }
            }
        }
        catch (JsonException ex)
        {
            problem = "not valid JSON (" + ex.Message + ")";
            return null;
        }

        problem = string.Empty;
        return new PriceTable
        {
            Source = PriceSources.OpenRouter,
            PricedOn = now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Models = kept,
        };
    }

    /// <summary>Whether an entry names a known online model and every value is in range and near its built-in price.</summary>
    public static bool Sane(ModelPrice price, IModelCatalog builtIn)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(builtIn);
        // Compared with the app's own price, not with the one in use: a downloaded price cannot move the limit day after day.
        if (price.Provider is null || price.Id is null || price.Provider == ModelProviders.Local || builtIn.BuiltIn(price.Provider, price.Id) is not { } model)
        {
            return false;
        }

        return Fits(price.InputUsd, model.InputUsd, PriceLimits.MaxTokenUsd) && Fits(price.OutputUsd, model.OutputUsd, PriceLimits.MaxTokenUsd)
            && Fits(price.CacheReadUsd, model.CacheReadUsd, PriceLimits.MaxTokenUsd) && Fits(price.CacheWriteUsd, model.CacheWriteUsd, PriceLimits.MaxTokenUsd)
            && Fits(price.LongInputUsd, model.LongInputUsd, PriceLimits.MaxTokenUsd) && Fits(price.LongOutputUsd, model.LongOutputUsd, PriceLimits.MaxTokenUsd)
            && Fits(price.ImageUsd, model.ImageUsd, PriceLimits.MaxImageUsd)
            && Fits(price.SpeechUsdPerMillionCharacters, model.SpeechUsdPerMillionCharacters, PriceLimits.MaxSpeechUsd);
    }

    private static bool Fits(decimal? value, decimal builtIn, decimal max)
    {
        if (value is not { } price)
        {
            return true;
        }

        if (price < 0 || price > max)
        {
            return false;
        }

        // Near the built-in price: a zero on either side cannot be compared (a new cache price, a model that became free).
        return price == 0 || builtIn == 0 || (price <= builtIn * PriceLimits.MaxChange && price * PriceLimits.MaxChange >= builtIn);
    }

    private static decimal? PerMillion(JsonElement pricing, string name)
    {
        if (!pricing.TryGetProperty(name, out var value))
        {
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var perToken))
        {
            return null;
        }

        // A negative price marks variable pricing (a router model): no price is better than a wrong one. An absurd one (it would overflow)
        // becomes a value over the limit, so the entry is dropped.
        return perToken < 0 ? null : perToken > PriceLimits.MaxTokenUsd ? PriceLimits.MaxTokenUsd + 1 : Math.Round(perToken * 1_000_000m, 6);
    }

    private static string Short(string? text)
    {
        var value = text ?? string.Empty;
        return value.Length <= 20 ? value : value[..20];
    }
}
