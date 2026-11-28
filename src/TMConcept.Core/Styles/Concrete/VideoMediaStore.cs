using System.Text.Json.Serialization.Metadata;
using TMConcept.Core.Models;
using TMConcept.Core.Render;

namespace TMConcept.Core.Styles;

/// <summary>
/// <see cref="IVideoMediaStore"/> over the profile's saved step choices (<see cref="ProfileChoiceStoreBase{T}"/>): the step
/// <see cref="Pipeline.PipelineSteps.VideoMedia"/> holds the <see cref="VideoMedia"/>. A damaged value reads as an empty choice.
/// </summary>
public sealed class VideoMediaStore(IStepModelStore steps) : ProfileChoiceStoreBase<VideoMedia>(steps), IVideoMediaStore
{
    /// <inheritdoc />
    protected override string Step => Pipeline.PipelineSteps.VideoMedia;

    /// <inheritdoc />
    protected override string Model => "media";

    /// <inheritdoc />
    protected override JsonTypeInfo<VideoMedia> Json => CoreJsonContext.Default.VideoMedia;

    /// <inheritdoc />
    protected override VideoMedia Empty() => new();

    /// <inheritdoc />
    protected override VideoMedia Normalize(VideoMedia value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Music ??= new VideoMusic();
        return value;
    }
}
