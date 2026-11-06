using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Keys;

namespace TMConcept.Core.Models;

/// <summary>
/// Finds the model and key of a step: a page's explicit choice when given, otherwise the profile's override, otherwise the general default.
/// A paid model is never picked automatically: no choice means <see cref="ModelNotReadyException"/> (or, for an optional step, "off").
/// A local model needs no key.
/// </summary>
public sealed class StepModelResolver(IStepModelStore steps, IApiKeyStore keys, IModelCatalog catalog, ILogger<StepModelResolver> logger)
{
    /// <summary>The choice that applies to a step: the profile's override (also an explicit "off"), then the general default; null = none.</summary>
    public async Task<StepModelChoice?> ChoiceAsync(string step, Guid? profileId, CancellationToken cancellationToken)
    {
        if (profileId is { } profile && await steps.GetAsync(step, profile, cancellationToken).ConfigureAwait(false) is { } own)
        {
            return own;
        }

        return await steps.GetAsync(step, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves the step for a profile (override → general default).</summary>
    public async Task<ResolvedModel> ResolveForAsync(string step, Guid? profileId, CancellationToken cancellationToken) =>
        await ResolveAsync(step, await ChoiceAsync(step, profileId, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <summary>An optional step for a profile: null when it is off or not set; a set model that is not ready still throws.</summary>
    public async Task<ResolvedModel?> ResolveOptionalAsync(string step, Guid? profileId, CancellationToken cancellationToken)
    {
        var choice = await ChoiceAsync(step, profileId, cancellationToken).ConfigureAwait(false);
        return IsOff(choice) ? null : await ResolveAsync(step, choice, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves a choice (a page's own one, or the saved one when <paramref name="choice"/> is null: the general default).</summary>
    public async Task<ResolvedModel> ResolveAsync(string step, StepModelChoice? choice, CancellationToken cancellationToken)
    {
        var chosen = choice ?? await steps.GetAsync(step, null, cancellationToken).ConfigureAwait(false);
        if (IsOff(chosen))
        {
            throw new ModelNotReadyException($"No model is chosen for the step '{step}'.") { Step = step, Missing = "model" };
        }

        var model = catalog.Find(chosen!.Provider, chosen.Model)
            ?? throw new ModelNotReadyException($"The model '{chosen.Provider}/{chosen.Model}' is not in the catalog.") { Step = step, Missing = "unknown" };
        var options = Options(chosen, out var damaged);
        if (damaged)
        {
            logger.LogWarning("The saved options of the step {Step} are damaged; the defaults are used", step);
        }

        if (model.Provider == ModelProviders.Local)
        {
            return new ResolvedModel(model, new ApiKey { Provider = ModelProviders.Local, Label = ModelProviders.Local }, options.Effort);
        }

        var all = await keys.ListAsync(model.Provider, cancellationToken).ConfigureAwait(false);
        var key = all.FirstOrDefault(k => k.Id == chosen.KeyId) ?? all.FirstOrDefault()
            ?? throw new ModelNotReadyException($"There is no API key for '{model.Provider}'.") { Step = step, Missing = "key" };
        return new ResolvedModel(model, key, options.Effort);
    }

    /// <summary>Whether a choice means "no model": none saved, an empty model, or an explicit off.</summary>
    public static bool IsOff(StepModelChoice? choice) => choice is null || choice.Provider == ModelProviders.Off || choice.Model.Length == 0;

    /// <summary>The options of a choice (defaults when the JSON is empty or damaged, or there is no choice).</summary>
    public static StepOptions Options(StepModelChoice? choice) => choice is null ? new StepOptions() : Options(choice, out _);

    /// <summary>The options of a choice; <paramref name="damaged"/> says the stored JSON could not be read (the defaults are returned).</summary>
    public static StepOptions Options(StepModelChoice choice, out bool damaged)
    {
        ArgumentNullException.ThrowIfNull(choice);
        damaged = false;
        if (string.IsNullOrWhiteSpace(choice.OptionsJson))
        {
            return new StepOptions();
        }

        try
        {
            return JsonSerializer.Deserialize(choice.OptionsJson, Render.CoreJsonContext.Default.StepOptions) ?? new StepOptions();
        }
        catch (JsonException)
        {
            damaged = true;
            return new StepOptions();
        }
    }

    /// <summary>Options as JSON.</summary>
    public static string OptionsJson(StepOptions options) => JsonSerializer.Serialize(options, Render.CoreJsonContext.Default.StepOptions);
}
