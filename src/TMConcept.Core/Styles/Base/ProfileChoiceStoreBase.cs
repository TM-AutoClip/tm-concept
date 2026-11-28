using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TMConcept.Core.Models;

namespace TMConcept.Core.Styles;

/// <summary>
/// A profile's choice of the video page kept among its saved step choices (no table of its own): one choice of a step that is not a model
/// step (<see cref="Step"/>), whose options hold the choice as JSON. It names no model of the catalog, so nothing installs or prices it. No
/// row or a damaged value reads as <see cref="Empty"/>. Shared by the background music and reference (<see cref="VideoMediaStore"/>, 0.1.54)
/// and the look (<see cref="VideoLookStore"/>, 28 Nov 2026), which differ only in their type and step.
/// </summary>
/// <typeparam name="T">The choice.</typeparam>
public abstract class ProfileChoiceStoreBase<T>(IStepModelStore steps)
    where T : class
{
    /// <summary>The provider written into the step choice (not a catalog provider).</summary>
    public const string Provider = "app";

    /// <summary>The step the choice is kept under (<see cref="Pipeline.PipelineSteps"/>).</summary>
    protected abstract string Step { get; }

    /// <summary>The model written into the step choice (not a catalog model).</summary>
    protected abstract string Model { get; }

    /// <summary>The source-generated JSON of the choice.</summary>
    protected abstract JsonTypeInfo<T> Json { get; }

    /// <summary>The profile's choice; <see cref="Empty"/> when none was saved or the saved one cannot be read.</summary>
    public async Task<T> GetAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var choice = await steps.GetAsync(Step, profileId, cancellationToken).ConfigureAwait(false);
        if (choice is null || string.IsNullOrWhiteSpace(choice.OptionsJson))
        {
            return Empty();
        }

        try
        {
            return JsonSerializer.Deserialize(choice.OptionsJson, Json) is { } read ? Normalize(read) : Empty();
        }
        catch (JsonException)
        {
            return Empty();
        }
    }

    /// <summary>Saves the profile's choice.</summary>
    public Task SaveAsync(Guid profileId, T value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        return steps.SaveAsync(new StepModelChoice
        {
            Step = Step,
            ProfileId = profileId,
            Provider = Provider,
            Model = Model,
            OptionsJson = JsonSerializer.Serialize(value, Json),
        }, cancellationToken);
    }

    /// <summary>The choice when nothing usable was saved.</summary>
    protected abstract T Empty();

    /// <summary>A read choice made whole (a missing part filled, an unknown value replaced); the default returns it as it is.</summary>
    protected virtual T Normalize(T value) => value;
}
