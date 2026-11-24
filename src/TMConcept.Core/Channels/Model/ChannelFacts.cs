using TMConcept.Core.Profiles;

namespace TMConcept.Core.Channels;

/// <summary>
/// What the numbers say about a channel (<see cref="ChannelDraftBuilder"/>): the target length (the median of its videos' lengths within the
/// profile's limits), its format (by the shape of its videos) and its language (by its titles); each null when the numbers do not tell (the
/// user chooses then).
/// </summary>
public sealed record ChannelFacts(int? TargetSeconds, VideoFormat? Format, string? Language);
