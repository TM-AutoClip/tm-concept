using TMConcept.Core.Engines;
using TMConcept.Core.Images;

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

    /// <summary>Subtitles burned into the picture.</summary>
    public bool Subtitles { get; set; } = true;

    /// <summary>The narration text file is written.</summary>
    public bool NarrationFile { get; set; } = true;
}
