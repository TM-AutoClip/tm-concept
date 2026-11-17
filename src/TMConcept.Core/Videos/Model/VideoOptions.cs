using TMConcept.Core.Engines;
using TMConcept.Core.Images;
using TMConcept.Core.Styles;

namespace TMConcept.Core.Videos;

/// <summary>The settings a video is made with (stored with the queue item and the video).</summary>
public sealed class VideoOptions
{
    /// <summary>Engine.</summary>
    public EngineKind Engine { get; set; } = EngineKind.HyperFrames;

    /// <summary>Code model provider.</summary>
    public string CodeProvider { get; set; } = string.Empty;

    /// <summary>Code model id.</summary>
    public string CodeModel { get; set; } = string.Empty;

    /// <summary>Where scene images come from.</summary>
    public string Images { get; set; } = ImageSources.None;

    /// <summary>Narration on.</summary>
    public bool Speech { get; set; } = true;

    /// <summary>
    /// Subtitles burned into the picture. Since templates it follows <see cref="Style"/> (set together by
    /// <see cref="Styles.VideoStyleRules.Snapshot"/>); read it through <see cref="Styles.VideoStyleRules.SubtitlesOn"/>.
    /// </summary>
    public bool Subtitles { get; set; } = true;

    /// <summary>
    /// The template the video was made with: a copy taken when the video started (a later edit or delete of the template does not change it;
    /// a new version from kept code keeps it). <c>null</c> for videos made before templates: they behave as before (<see cref="Subtitles"/>,
    /// no progress bar, the default look).
    /// </summary>
    public VideoStyle? Style { get; set; }
}
