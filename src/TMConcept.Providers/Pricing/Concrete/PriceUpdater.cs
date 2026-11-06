using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;
using TMConcept.Core.Providers;
using TMConcept.Providers.Catalog;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Pricing;

/// <summary>
/// Keeps the prices current without the user typing any: the built-in table is the fallback; at most once a day the repository's prices
/// file (<see cref="RepositoryFile"/>) and OpenRouter's public models list (<see cref="OpenRouterModels"/>, no key) are read, checked
/// (<see cref="PriceCheck"/>) and kept as the last good copy in <c>&lt;data&gt;/prices</c>. Every network part is optional: offline, or with a
/// refused file, the last good copy (or the built-in table) stays. No key, no personal data is sent; outcomes are logged.
/// </summary>
public sealed class PriceUpdater(ProviderHttp http, IModelCatalog catalog, IAppPaths paths, TimeProvider clock, ILogger<PriceUpdater> logger)
{
    /// <summary>The prices file of the public repository.</summary>
    public static readonly Uri RepositoryFile = new("https://raw.githubusercontent.com/TM-AutoClip/tm-concept/main/pricing/prices.json");

    /// <summary>OpenRouter's public models list.</summary>
    public static readonly Uri OpenRouterModels = new("https://openrouter.ai/api/v1/models");

    /// <summary>Least time between two checks.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private const string RepositoryCopy = "repository.json";
    private const string OpenRouterCopy = "openrouter.json";
    private const string CheckedFile = "checked.txt";

    /// <summary>The folder of the last good copies.</summary>
    public string Folder => Path.Combine(paths.DataDirectory, "prices");

    /// <summary>Applies the last good copies (no network); called at start, before anything is priced.</summary>
    public void LoadSaved()
    {
        var tables = new List<PriceTable>();
        foreach (var (file, source) in new[] { (RepositoryCopy, PriceSources.Repository), (OpenRouterCopy, PriceSources.OpenRouter) })
        {
            var path = Path.Combine(Folder, file);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                // The saved copy gets the checks of a download again: a copy older than this app's built-in table (the app was updated
                // since) would bring old prices back, and a damaged file must not be applied.
                var saved = JsonSerializer.Deserialize(File.ReadAllText(path), ProvidersJsonContext.Default.PriceTable);
                var problem = "empty";
                if (saved is null || PriceCheck.Check(saved, source, catalog, clock.GetUtcNow(), out problem, out _) is not { } table)
                {
                    logger.LogInformation("The saved {Source} prices are not used ({Problem}); the built-in prices are used for them", source, problem);
                    continue;
                }

                tables.Add(table);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning(ex, "The saved {Source} prices could not be read; the built-in prices are used for them", source);
            }
        }

        if (tables.Count > 0)
        {
            catalog.ApplyPrices(Order(tables));
            logger.LogInformation("Prices: {Sources}", string.Join(", ", tables.Select(t => t.Source + " " + t.PricedOn)));
        }
    }

    /// <summary>
    /// Reads both sources when the last check is a day old or more (<paramref name="force"/> skips the wait); returns whether a check ran.
    /// Never throws for a network or file problem: it is logged and the last good copy stays.
    /// </summary>
    public async Task<bool> UpdateAsync(bool force, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var checkedFile = Path.Combine(Folder, CheckedFile);
        if (!force && LastCheck(checkedFile) is { } last && now - last < Interval)
        {
            return false;
        }

        Directory.CreateDirectory(Folder);
        var changed = await FetchAsync(RepositoryFile, "GitHub", RepositoryCopy, json =>
            PriceCheck.ReadFile(json, catalog, now, out var problem, out var dropped) is { } table ? (table, problem, dropped) : (null, problem, dropped),
            cancellationToken).ConfigureAwait(false);
        changed |= await FetchAsync(OpenRouterModels, "OpenRouter", OpenRouterCopy, json =>
            PriceCheck.ReadOpenRouter(json, catalog, now, out var problem, out var dropped) is { } table ? (table, problem, dropped) : (null, problem, dropped),
            cancellationToken).ConfigureAwait(false);
        if (changed)
        {
            LoadSaved();
        }

        // Written after both sources were read: a check that did not finish runs again at the next start.
        await File.WriteAllTextAsync(checkedFile, now.ToString("O", CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> FetchAsync(Uri address, string name, string copy, Func<string, (PriceTable? Table, string Problem, int Dropped)> read,
        CancellationToken cancellationToken)
    {
        string json;
        try
        {
            var reply = await http.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, address), name, string.Empty, cancellationToken).ConfigureAwait(false);
            json = Encoding.UTF8.GetString(reply.Body);
        }
        catch (ProviderException ex)
        {
            logger.LogWarning("Prices from {Source} were not updated ({Code}); the last good copy stays", name, ex.Code);
            return false;
        }

        PriceTable? table;
        string problem;
        int dropped;
        try
        {
            (table, problem, dropped) = read(json);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            // Untrusted input: whatever the checks missed refuses the file, it never stops the other source or the app.
            logger.LogWarning(ex, "Prices from {Source} were refused: the file could not be checked; the last good copy stays", name);
            return false;
        }

        if (table is null)
        {
            logger.LogWarning("Prices from {Source} were refused: {Problem}; the last good copy stays", name, problem);
            return false;
        }

        // Nothing usable (every entry dropped, or no known model in the list): the last good copy is better than a new date with no prices.
        if (table.Models.Count == 0)
        {
            logger.LogWarning("Prices from {Source} were refused: no usable entry ({Dropped} dropped); the last good copy stays", name, dropped);
            return false;
        }

        if (dropped > 0)
        {
            logger.LogWarning("Prices from {Source}: {Dropped} entries were out of range and dropped", name, dropped);
        }

        try
        {
            var target = Path.Combine(Folder, copy);
            await File.WriteAllTextAsync(target + ".new", JsonSerializer.Serialize(table, ProvidersJsonContext.Default.PriceTable), cancellationToken)
                .ConfigureAwait(false);
            File.Move(target + ".new", target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The prices from {Source} could not be saved", name);
            return false;
        }

        logger.LogInformation("Prices from {Source} updated: {Count} models, dated {Date}", name, table.Models.Count, table.PricedOn);
        return true;
    }

    private DateTimeOffset? LastCheck(string file)
    {
        try
        {
            return File.Exists(file) && DateTimeOffset.TryParse(File.ReadAllText(file).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var at) ? at : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The time of the last price check could not be read");
            return null;
        }
    }

    // The repository file first, OpenRouter's own list after it: for an OpenRouter model the live list wins.
    private static List<PriceTable> Order(List<PriceTable> tables) =>
        tables.OrderBy(t => t.Source == PriceSources.OpenRouter ? 1 : 0).ToList();
}
