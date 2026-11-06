using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TMConcept.Core.Models;
using TMConcept.Core.Runs;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The general default of every step (a profile can override any of them on its own page). Saving installs what a chosen setting needs
/// (see <see cref="StepSettingsViewModel"/>). Also: where the prices come from and their date (updated by themselves), and this month's
/// spending: concept runs per provider and the videos (no limits or balances are shown).
/// </summary>
public sealed partial class ModelsSettingsViewModel(StepSettingsViewModel steps, IModelCatalog catalog, IRunStore runs, Core.Profiles.IProfileStore profiles,
    Core.Videos.IVideoStore videos, Localizer text, TimeProvider clock)
    : SettingsSectionBase
{
    /// <inheritdoc />
    public override string TitleKey => "settings.models";

    /// <inheritdoc />
    public override int Order => 30;

    /// <summary>The step table.</summary>
    public StepSettingsViewModel Steps { get; } = steps;

    /// <summary>The steps (shortcut for tests).</summary>
    public System.Collections.ObjectModel.ObservableCollection<StepModelRowViewModel> Rows => Steps.Rows;

    /// <summary>Spending this month.</summary>
    [ObservableProperty]
    public partial string Spending { get; set; } = string.Empty;

    /// <summary>"Prices: date (source)".</summary>
    [ObservableProperty]
    public partial string PricedOn { get; set; } = string.Empty;

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        await Steps.LoadAsync(null, text.Language == "tr" ? Core.Profiles.ContentLanguages.Turkish : Core.Profiles.ContentLanguages.English)
            .ConfigureAwait(true);
        PricedOn = PriceText(catalog, text);
        await SpendingAsync().ConfigureAwait(true);
    }

    /// <summary>Saves every changed step (after installing what it needs).</summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        await Steps.SaveAsync().ConfigureAwait(true);
        Status = Steps.Status;
    }

    /// <summary>"Prices: 2026-11-06 (built-in table) · OpenRouter: 2026-11-07".</summary>
    public static string PriceText(IModelCatalog catalog, Localizer text)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(text);
        var tables = catalog.Prices;
        var main = tables.LastOrDefault(t => t.Source != PriceSources.OpenRouter) ?? tables[0];
        var line = text.Format("models.prices", main.PricedOn.Length > 0 ? main.PricedOn : "-", text["models.prices." + main.Source]);
        if (tables.FirstOrDefault(t => t.Source == PriceSources.OpenRouter) is { } openRouter)
        {
            line += " · " + text.Format("models.prices.openrouter_date", openRouter.PricedOn);
        }

        return line;
    }

    private async Task SpendingAsync()
    {
        var now = clock.GetUtcNow();
        var month = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var list = await runs.ListAsync(month, CancellationToken.None).ConfigureAwait(true);
        var parts = list.GroupBy(r => r.Provider).Where(g => g.Key.Length > 0 && g.Sum(r => r.ActualUsd) > 0)
            .Select(g => g.Key + " " + text.Usd(g.Sum(r => r.ActualUsd))).ToList();
        decimal made = 0;
        foreach (var profile in await profiles.ListAsync(CancellationToken.None).ConfigureAwait(true))
        {
            made += (await videos.ListAsync(profile.Id, CancellationToken.None).ConfigureAwait(true)).Where(v => v.CreatedAtUtc >= month).Sum(v => v.TotalUsd);
        }

        if (made > 0)
        {
            parts.Add(text.Format("models.spending.videos", text.Usd(made)));
        }

        var total = text.Usd(list.Sum(r => r.ActualUsd) + made);
        Spending = parts.Count == 0 ? text.Format("models.spending.total", total) : text.Format("models.spending", total, string.Join(" · ", parts));
    }
}
