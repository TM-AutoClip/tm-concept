using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Runs;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Default model of every step. The choice starts empty and shows a "Recommended" note: a paid model is never chosen automatically.
/// Also: the date of the price table, a price override for a model whose price changed (or is missing), and this month's spending per
/// provider (from the runs; no limits or balances are shown).
/// </summary>
public sealed partial class ModelsSettingsViewModel(IModelCatalog catalog, IStepModelStore steps, IPriceOverrideStore prices, IRunStore runs,
    Localizer text, TimeProvider clock, ILogger<ModelsSettingsViewModel> logger) : SettingsSectionBase
{
    /// <inheritdoc />
    public override string TitleKey => "settings.models";

    /// <inheritdoc />
    public override int Order => 30;

    /// <summary>Steps.</summary>
    public ObservableCollection<StepModelRowViewModel> Rows { get; } = [];

    /// <summary>Spending this month.</summary>
    [ObservableProperty]
    public partial string Spending { get; set; } = string.Empty;

    /// <summary>Price table note.</summary>
    [ObservableProperty]
    public partial string PricedOn { get; set; } = string.Empty;

    /// <summary>Override: provider code.</summary>
    [ObservableProperty]
    public partial string OverrideProvider { get; set; } = string.Empty;

    /// <summary>Override: model id.</summary>
    [ObservableProperty]
    public partial string OverrideModel { get; set; } = string.Empty;

    /// <summary>Override: input price.</summary>
    [ObservableProperty]
    public partial decimal? OverrideInput { get; set; }

    /// <summary>Override: output price.</summary>
    [ObservableProperty]
    public partial decimal? OverrideOutput { get; set; }

    /// <summary>Override: cache read price.</summary>
    [ObservableProperty]
    public partial decimal? OverrideCacheRead { get; set; }

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        Rows.Clear();
        var saved = await steps.ListAsync(CancellationToken.None).ConfigureAwait(true);
        foreach (var step in PipelineSteps.All)
        {
            var recommended = step.Recommended.Select(r => Label(catalog.All.FirstOrDefault(m => m.Provider + "/" + m.Id == r)) ?? r);
            var row = new StepModelRowViewModel(step, text["step." + step.Code],
                step.Recommended.Count > 0 ? text.Format("models.recommended", string.Join(" · ", recommended)) : text["models.recommended.local"]);
            row.Options.Add(new ModelOption(null, text["models.none"]));
            foreach (var model in catalog.With(step.Capability).OrderBy(m => m.Provider, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal))
            {
                row.Options.Add(new ModelOption(model, Label(model)!));
            }

            var choice = saved.FirstOrDefault(s => s.Step == step.Code);
            row.Selected = row.Options.FirstOrDefault(o => o.Model is { } m && choice is not null && m.Provider == choice.Provider && m.Id == choice.Model) ?? row.Options[0];
            row.Effort = choice is null ? string.Empty : StepModelResolver.Options(choice).Effort;
            Rows.Add(row);
        }

        PricedOn = text.Format("models.priced_on", catalog.All.Select(m => m.PricedOn).Where(d => d.Length > 0).DefaultIfEmpty("-").Max()!);
        await SpendingAsync().ConfigureAwait(true);
    }

    /// <summary>Saves every step's default.</summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        foreach (var row in Rows)
        {
            logger.LogInformation("Setting changed: step {Step} uses {Model} (effort {Effort})", row.Step.Code,
                row.Selected?.Model is { } chosen ? chosen.Provider + "/" + chosen.Id : "no model", row.Effort is { Length: > 0 } effort ? effort : "default");
            if (row.Selected?.Model is { } model)
            {
                await steps.SaveAsync(new StepModelChoice
                {
                    Step = row.Step.Code,
                    Provider = model.Provider,
                    Model = model.Id,
                    OptionsJson = StepModelResolver.OptionsJson(new StepOptions { Effort = row.Effort ?? string.Empty }),
                }, CancellationToken.None).ConfigureAwait(true);
            }
            else
            {
                await steps.ClearAsync(row.Step.Code, CancellationToken.None).ConfigureAwait(true);
            }
        }

        Status = text["common.saved"];
    }

    /// <summary>Saves a price override and applies it at once.</summary>
    [RelayCommand]
    public async Task SaveOverrideAsync()
    {
        if (OverrideProvider.Trim().Length == 0 || OverrideModel.Trim().Length == 0 || OverrideInput is null || OverrideOutput is null)
        {
            Status = text["models.override.invalid"];
            return;
        }

        await prices.SaveAsync(new ModelPriceOverride
        {
            Provider = OverrideProvider.Trim(),
            ModelId = OverrideModel.Trim(),
            InputUsd = OverrideInput.Value,
            OutputUsd = OverrideOutput.Value,
            CacheReadUsd = OverrideCacheRead ?? 0,
        }, CancellationToken.None).ConfigureAwait(true);
        logger.LogInformation("Setting changed: price override for {Provider}/{Model}", OverrideProvider.Trim(), OverrideModel.Trim());
        catalog.UseOverrides(await prices.ListAsync(CancellationToken.None).ConfigureAwait(true));
        await ActivateAsync().ConfigureAwait(true);
        Status = text["common.saved"];
    }

    private async Task SpendingAsync()
    {
        var now = clock.GetUtcNow();
        var month = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var list = await runs.ListAsync(month, CancellationToken.None).ConfigureAwait(true);
        var total = list.Sum(r => r.ActualUsd);
        var parts = list.GroupBy(r => r.Provider).Where(g => g.Key.Length > 0).Select(g => g.Key + " " + text.Usd(g.Sum(r => r.ActualUsd)));
        Spending = text.Format("models.spending", text.Usd(total), string.Join(" · ", parts));
    }

    private string? Label(CatalogModel? model)
    {
        if (model is null)
        {
            return null;
        }

        var price = model.Capabilities.HasFlag(ModelCapability.Image)
            ? text.Usd(model.ImageUsd) + "/img"
            : model.Capabilities.HasFlag(ModelCapability.Speech)
                ? text.Usd(model.SpeechUsdPerMillionCharacters) + "/1M chr"
                : string.Create(CultureInfo.InvariantCulture, $"{model.InputUsd:0.##} / {model.OutputUsd:0.##} $");
        return model.Name + " · " + model.Provider + " · " + price;
    }
}
