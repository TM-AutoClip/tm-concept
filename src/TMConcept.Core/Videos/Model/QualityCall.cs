using TMConcept.Core.Models;

namespace TMConcept.Core.Videos;

/// <summary>
/// Who a review call goes to and who pays (F11, 0.1.50): the quality check step's model, the run's progress (every call is spent there, also a
/// local one at no cost) and the video's id.
/// </summary>
public sealed record QualityCall(ResolvedModel Model, VideoStepProgress Steps, Guid VideoId);
