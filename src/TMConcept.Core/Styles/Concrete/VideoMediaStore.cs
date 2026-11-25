using System.Text.Json;
using TMConcept.Core.Models;
using TMConcept.Core.Render;

namespace TMConcept.Core.Styles;

/// <summary>
/// <see cref="IVideoMediaStore"/> over the profile's saved step choices (no new table): one choice of the step
/// <see cref="Pipeline.PipelineSteps.VideoMedia"/> whose options hold the <see cref="VideoMedia"/> as JSON. It names no model of the catalog,
/// so nothing installs or prices it. A damaged value reads as an empty choice.
/// </summary>
public sealed class VideoMediaStore(IStepModelStore steps) : IVideoMediaStore
{
    /// <summary>The provider written into the step choice (not a catalog provider).</summary>
    public const string Provider = "app";

    /// <summary>The model written into the step choice (not a catalog model).</summary>
    public const string Model = "media";

    /// <inheritdoc />
    public async Task<VideoMedia> GetAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var choice = await steps.GetAsync(Pipeline.PipelineSteps.VideoMedia, profileId, cancellationToken).ConfigureAwait(false);
        if (choice is null || string.IsNullOrWhiteSpace(choice.OptionsJson))
        {
            return new VideoMedia();
        }

        try
        {
            var media = JsonSerializer.Deserialize(choice.OptionsJson, CoreJsonContext.Default.VideoMedia) ?? new VideoMedia();
            media.Music ??= new VideoMusic();
            return media;
        }
        catch (JsonException)
        {
            return new VideoMedia();
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(Guid profileId, VideoMedia media, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        return steps.SaveAsync(new StepModelChoice
        {
            Step = Pipeline.PipelineSteps.VideoMedia,
            ProfileId = profileId,
            Provider = Provider,
            Model = Model,
            OptionsJson = JsonSerializer.Serialize(media, CoreJsonContext.Default.VideoMedia),
        }, cancellationToken);
    }
}
