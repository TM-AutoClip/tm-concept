namespace TMConcept.Core.Styles;

/// <summary>
/// A profile's (a series') background music and reference video (0.1.54, the developer: the template rarely changes — colours, the channel's
/// look — while the reference video and the music change from video to video). Chosen on the video page's model choice, kept per profile
/// (<see cref="IVideoMediaStore"/>) and put into each starting video's template copy (<see cref="VideoStyleRules.Snapshot(Videos.VideoOptions, VideoStyle, VideoMedia?)"/>).
/// Before 0.1.54 they were part of the template; a template's old values are not carried over.
/// </summary>
public sealed class VideoMedia
{
    /// <summary>The background music (off by default).</summary>
    public VideoMusic Music { get; set; } = new();

    /// <summary>The measured reference video ("bu videonun temposunda yap"); null = none.</summary>
    public References.ReferenceTempoProfile? Reference { get; set; }
}
