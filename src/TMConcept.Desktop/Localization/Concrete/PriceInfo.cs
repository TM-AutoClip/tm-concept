using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Runs;
using TMConcept.Core.Videos;

namespace TMConcept.Desktop.Localization;

/// <summary>
/// Price lines shown next to the estimates (since 0.1.6 Settings has nothing about models): where the prices come from and their date, and
/// this month's spending (concept runs per provider, the videos, and what failed videos paid; no limits or balances), which the spending
/// pill of the main window shows (<see cref="Work.SpendingMeter"/>).
/// </summary>
public static class PriceInfo
{
    /// <summary>"Prices: 2026-11-06 (built-in table) · OpenRouter: 2026-11-07".</summary>
    public static string Text(IModelCatalog catalog, Localizer text)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(text);
        var main = MainTable(catalog);
        var line = text.Format("models.prices", Date(catalog), text["models.prices." + main.Source]);
        if (catalog.Prices.FirstOrDefault(t => t.Source == PriceSources.OpenRouter) is { } openRouter)
        {
            line += " · " + text.Format("models.prices.openrouter_date", openRouter.PricedOn);
        }

        return line;
    }

    /// <summary>The date of the main price table (the built-in one or the repository's newer file); "-" when it has none.</summary>
    public static string Date(IModelCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var main = MainTable(catalog);
        return main.PricedOn.Length > 0 ? main.PricedOn : "-";
    }

    /// <summary>This month's spending of every profile: the total and its parts per provider, the made videos and the failed videos.</summary>
    public static async Task<Spending> SpendingAsync(IRunStore runs, IProfileStore profiles, IVideoStore videos, TimeProvider clock, Localizer text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(videos);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(text);
        var now = clock.GetUtcNow();
        var month = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var list = await runs.ListAsync(month, cancellationToken).ConfigureAwait(true);
        // What failed videos paid is a part of its own (its narration, images and code may come from several providers).
        var parts = list.Where(r => r.Kind != RunKind.FailedVideo).GroupBy(r => r.Provider).Where(g => g.Key.Length > 0 && g.Sum(r => r.ActualUsd) > 0)
            .Select(g => g.Key + " " + text.Usd(g.Sum(r => r.ActualUsd))).ToList();
        var lost = list.Where(r => r.Kind == RunKind.FailedVideo).Sum(r => r.ActualUsd);
        decimal made = 0;
        foreach (var profile in await profiles.ListAsync(cancellationToken).ConfigureAwait(true))
        {
            made += (await videos.ListAsync(profile.Id, cancellationToken).ConfigureAwait(true)).Where(v => v.CreatedAtUtc >= month).Sum(v => v.TotalUsd);
        }

        if (made > 0)
        {
            parts.Add(text.Format("models.spending.videos", text.Usd(made)));
        }

        if (lost > 0)
        {
            parts.Add(text.Format("models.spending.failed", text.Usd(lost)));
        }

        return new Spending(list.Sum(r => r.ActualUsd) + made, string.Join(" · ", parts));
    }

    private static PriceTable MainTable(IModelCatalog catalog) =>
        catalog.Prices.LastOrDefault(t => t.Source != PriceSources.OpenRouter) ?? catalog.Prices[0];
}
