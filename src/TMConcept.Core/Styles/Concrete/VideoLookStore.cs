using System.Text.Json.Serialization.Metadata;
using TMConcept.Core.Models;
using TMConcept.Core.Render;

namespace TMConcept.Core.Styles;

/// <summary>
/// <see cref="IVideoLookStore"/> over the profile's saved step choices (<see cref="ProfileChoiceStoreBase{T}"/>): the step
/// <see cref="Pipeline.PipelineSteps.VideoLook"/> holds the <see cref="VideoLook"/>. A damaged value, or an unknown style or motion, reads as
/// the default look (the AI chooses, normal motion, finishing on).
/// </summary>
public sealed class VideoLookStore(IStepModelStore steps) : ProfileChoiceStoreBase<VideoLook>(steps), IVideoLookStore
{
    /// <inheritdoc />
    protected override string Step => Pipeline.PipelineSteps.VideoLook;

    /// <inheritdoc />
    protected override string Model => "look";

    /// <inheritdoc />
    protected override JsonTypeInfo<VideoLook> Json => CoreJsonContext.Default.VideoLook;

    /// <inheritdoc />
    protected override VideoLook Empty() => new();

    /// <inheritdoc />
    protected override VideoLook Normalize(VideoLook value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Enum.IsDefined(value.DrawStyle) && Enum.IsDefined(value.Motion) ? value : Empty();
    }
}
