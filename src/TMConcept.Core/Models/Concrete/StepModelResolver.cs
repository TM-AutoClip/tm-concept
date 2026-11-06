using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Keys;

namespace TMConcept.Core.Models;

/// <summary>
/// Finds the model and key of a step: the page's choice when given, otherwise the step's saved default. A paid model is never picked
/// automatically: no choice means <see cref="ModelNotReadyException"/>.
/// </summary>
public sealed class StepModelResolver(IStepModelStore steps, IApiKeyStore keys, IModelCatalog catalog, ILogger<StepModelResolver> logger)
{
    /// <summary>Resolves; <paramref name="choice"/> overrides the saved default.</summary>
    public async Task<ResolvedModel> ResolveAsync(string step, StepModelChoice? choice, CancellationToken cancellationToken)
    {
        var chosen = choice ?? await steps.GetAsync(step, cancellationToken).ConfigureAwait(false);
        if (chosen is null || chosen.Model.Length == 0)
        {
            throw new ModelNotReadyException($"No model is chosen for the step '{step}'.") { Step = step, Missing = "model" };
        }

        var model = catalog.Find(chosen.Provider, chosen.Model)
            ?? throw new ModelNotReadyException($"The model '{chosen.Provider}/{chosen.Model}' is not in the catalog.") { Step = step, Missing = "unknown" };
        var all = await keys.ListAsync(model.Provider, cancellationToken).ConfigureAwait(false);
        var key = all.FirstOrDefault(k => k.Id == chosen.KeyId) ?? all.FirstOrDefault()
            ?? throw new ModelNotReadyException($"There is no API key for '{model.Provider}'.") { Step = step, Missing = "key" };
        var options = Options(chosen, out var damaged);
        if (damaged)
        {
            logger.LogWarning("The saved options of the step {Step} are damaged; the defaults are used", step);
        }

        return new ResolvedModel(model, key, options.Effort);
    }

    /// <summary>The options of a choice (defaults when the JSON is empty or damaged).</summary>
    public static StepOptions Options(StepModelChoice choice) => Options(choice, out _);

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
