using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Keys;

namespace TMConcept.Core.Models;

/// <summary>
/// Finds the model and key of a step: a page's explicit choice when given, otherwise the profile's saved choice (the last one used on the
/// page). A paid model is never picked automatically: no choice means <see cref="ModelNotReadyException"/> (or, for an optional step,
/// "off"). A local model needs no key.
/// </summary>
public sealed class StepModelResolver(IStepModelStore steps, IApiKeyStore keys, IModelCatalog catalog, ILogger<StepModelResolver> logger)
{
    /// <summary>The profile's saved choice of a step (also an explicit "off"); null = none, or no profile.</summary>
    public async Task<StepModelChoice?> ChoiceAsync(string step, Guid? profileId, CancellationToken cancellationToken) =>
        profileId is { } profile ? await steps.GetAsync(step, profile, cancellationToken).ConfigureAwait(false) : null;

    /// <summary>Resolves the step for a profile (its saved choice).</summary>
    public async Task<ResolvedModel> ResolveForAsync(string step, Guid? profileId, CancellationToken cancellationToken) =>
        await ResolveAsync(step, await ChoiceAsync(step, profileId, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <summary>An optional step for a profile: null when it is off or not set; a set model that is not ready still throws.</summary>
    public async Task<ResolvedModel?> ResolveOptionalAsync(string step, Guid? profileId, CancellationToken cancellationToken)
    {
        var choice = await ChoiceAsync(step, profileId, cancellationToken).ConfigureAwait(false);
        return IsOff(choice) ? null : await ResolveAsync(step, choice, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves a choice (a page's own one, or a saved one); none or "off" = not ready.</summary>
    public async Task<ResolvedModel> ResolveAsync(string step, StepModelChoice? choice, CancellationToken cancellationToken)
    {
        if (IsOff(choice))
        {
            throw new ModelNotReadyException($"No model is chosen for the step '{step}'.") { Step = step, Missing = "model" };
        }

        var model = catalog.Find(choice!.Provider, choice.Model)
            ?? throw new ModelNotReadyException($"The model '{choice.Provider}/{choice.Model}' is not in the catalog.") { Step = step, Missing = "unknown" };
        var options = Options(choice, out var damaged);
        if (damaged)
        {
            logger.LogWarning("The saved options of the step {Step} are damaged; the defaults are used", step);
        }

        if (model.Provider == ModelProviders.Local)
        {
            return new ResolvedModel(model, new ApiKey { Provider = ModelProviders.Local, Label = ModelProviders.Local }, options.Effort);
        }

        var all = await keys.ListAsync(model.Provider, cancellationToken).ConfigureAwait(false);
        var key = all.FirstOrDefault(k => k.Id == choice.KeyId) ?? all.FirstOrDefault()
            ?? throw new ModelNotReadyException($"There is no API key for '{model.Provider}'.") { Step = step, Missing = "key" };
        return new ResolvedModel(model, key, options.Effort);
    }

    /// <summary>
    /// The online providers among <paramref name="models"/> that have no API key yet (each once, in order): the key gate before a process
    /// starts. Local models need none.
    /// </summary>
    public async Task<IReadOnlyList<string>> ProvidersWithoutKeyAsync(IEnumerable<CatalogModel> models, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(models);
        var missing = new List<string>();
        foreach (var provider in models.Where(m => m.Provider != ModelProviders.Local).Select(m => m.Provider).Distinct(StringComparer.Ordinal))
        {
            if ((await keys.ListAsync(provider, cancellationToken).ConfigureAwait(false)).Count == 0)
            {
                missing.Add(provider);
            }
        }

        return missing;
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
