using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Keys;
using TMConcept.Core.Providers;

namespace TMConcept.Core.Models;

/// <summary>
/// Finds the model and key of a step: a page's explicit choice when given, otherwise the profile's saved choice (the last one used on the
/// page). A paid model is never picked automatically: no choice means <see cref="ModelNotReadyException"/> (or, for an optional step,
/// "off"). A local model needs no key. Of several keys of a provider, the chosen one is used, else one that passed its test.
/// </summary>
public sealed class StepModelResolver(IStepModelStore steps, IApiKeyStore keys, IModelCatalog catalog, IEnumerable<IProvider> providers,
    TimeProvider clock, ILogger<StepModelResolver> logger)
{
    private readonly IReadOnlyList<IProvider> _providers = providers.ToList();

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
        var key = all.FirstOrDefault(k => k.Id == choice.KeyId) ?? all.Where(k => k.LastOkAtUtc is not null).MaxBy(k => k.LastOkAtUtc) ?? all.FirstOrDefault()
            ?? throw new ModelNotReadyException($"There is no API key for '{model.Provider}'.") { Step = step, Missing = "key" };
        return new ResolvedModel(model, key, options.Effort);
    }

    /// <summary>
    /// The key gate before a process starts: the online providers among <paramref name="models"/> (each once, in order) without a WORKING
    /// key. A key that passed its test counts; a provider with no free test (fal) counts with any key (checked at its first use); otherwise
    /// the provider's keys are tested now, newest first, with the free request (a passing one is marked and counts). No key, or only keys
    /// that fail (a wrong or expired key saved in 0.1.5, a restored one), = the provider is returned and the process must not start.
    /// </summary>
    public async Task<IReadOnlyList<string>> ProvidersWithoutWorkingKeyAsync(IEnumerable<CatalogModel> models, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(models);
        var missing = new List<string>();
        foreach (var provider in models.Where(m => m.Provider != ModelProviders.Local).Select(m => m.Provider).Distinct(StringComparer.Ordinal))
        {
            if (!await HasWorkingKeyAsync(provider, cancellationToken).ConfigureAwait(false))
            {
                missing.Add(provider);
            }
        }

        return missing;
    }

    /// <summary>Whether the provider has a free key test (false: its keys are saved untested and checked at their first use).</summary>
    public bool CanTest(string provider) => _providers.FirstOrDefault(p => p.Code == provider)?.CanTest ?? false;

    /// <summary>
    /// The online providers among <paramref name="models"/> that have no API key at all (each once, in order; no request is made): the note
    /// "a key is asked before the process starts". The gate itself is <see cref="ProvidersWithoutWorkingKeyAsync"/>.
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

    private async Task<bool> HasWorkingKeyAsync(string provider, CancellationToken cancellationToken)
    {
        var all = await keys.ListAsync(provider, cancellationToken).ConfigureAwait(false);
        if (all.Count == 0)
        {
            logger.LogWarning("Key gate: {Provider} has no API key", provider);
            return false;
        }

        if (all.Any(k => k.LastOkAtUtc is not null))
        {
            return true;
        }

        if (_providers.FirstOrDefault(p => p.Code == provider) is not { CanTest: true } tester)
        {
            logger.LogInformation("Key gate: {Provider} has no free key test; its key is checked at its first use", provider);
            return true;
        }

        foreach (var key in all.OrderByDescending(k => k.CreatedAtUtc))
        {
            var result = await tester.TestAsync(key.Secret, cancellationToken).ConfigureAwait(false);
            if (result.Ok)
            {
                if (result.Code == "ok")
                {
                    await keys.MarkOkAsync(key.Id, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                }

                logger.LogInformation("Key gate: a saved key of {Provider} passed its test", provider);
                return true;
            }

            logger.LogWarning("Key gate: a saved key of {Provider} failed its test ({Code})", provider, result.Code);
        }

        return false;
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
