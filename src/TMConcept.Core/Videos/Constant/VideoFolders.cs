namespace TMConcept.Core.Videos;

/// <summary>
/// The layout of a made video in the library folder. Every run that makes a video writes one version folder:
/// <c>&lt;library&gt;/&lt;profile&gt;/&lt;date&gt;-&lt;number&gt;-&lt;title&gt;/v1/</c> with the finished files (video, thumbnail, subtitles,
/// narration text), <see cref="Source"/> (the engine project exactly as it rendered: the app's skeleton, the model's code file,
/// <c>input.json</c> with the timing, the scene images) and <see cref="Audio"/> (the narration clip of each scene). That is everything a
/// later version needs: a new voice made on this computer and the same code rendered again (v2/, v3/… next to it, no model call), or
/// new code for another engine with the same images, clips and texts. Each version has its own video record pointing at its folder;
/// older versions are never changed (<see cref="VersionFolders"/> names and numbers them).
/// </summary>
public static class VideoFolders
{
    /// <summary>The first version's folder.</summary>
    public const string FirstVersion = "v1";

    /// <summary>The start of every version folder's name ("v" + number).</summary>
    public const string VersionPrefix = "v";

    /// <summary>The engine project as it rendered.</summary>
    public const string Source = "source";

    /// <summary>The narration clips (<c>1.wav</c>, <c>2.wav</c>, … by scene number).</summary>
    public const string Audio = "audio";

    /// <summary>The video.</summary>
    public const string VideoFile = "video.mp4";

    /// <summary>The subtitles (always written).</summary>
    public const string SubtitlesFile = "subtitles.srt";

    /// <summary>The narration text (always written, also for a silent video).</summary>
    public const string NarrationFile = "narration.txt";

    /// <summary>
    /// The edited narration of a version made with a new voice: one text per scene WITH its narration markers (the subtitles and
    /// <c>input.json</c> have them taken out), so the next edit starts from what was written, markers included.
    /// </summary>
    public const string ScriptFile = "narration-edit.json";

    /// <summary>The frame strips of the motion check's findings (0.1.41; PNG, one per finding, named by its kind and second).</summary>
    public const string Checks = "checks";

    /// <summary>
    /// Inside <see cref="Checks"/> (0.1.48): the subtitle sentences that matched the narration poorly when the words were heard, with their
    /// times and match share (written only when there is one; <see cref="VideoWarnings.SubtitleListen"/>).
    /// </summary>
    public const string SubtitleCheckFile = "subtitle-check.txt";

    /// <summary>
    /// The background music of the version (0.1.44): the track as it was mixed in, copied here so a later version (a new voice, another
    /// engine) uses the same music without asking for it again.
    /// </summary>
    public const string Music = "music";

    /// <summary>
    /// The version's asset record (0.1.44, F15; <see cref="AssetLedger"/>): every outside file the video uses (music, pictures) with its
    /// source, licence and the attribution it asks for. Always written; an empty list when there is none.
    /// </summary>
    public const string AssetsFile = "assets.json";

    /// <summary>
    /// The notes left on the version while it played (F16, 0.1.49; <see cref="TimeNotes"/>): each with its moment, its scene and its text. Written
    /// only when there is one; "Fix" makes a new version from them.
    /// </summary>
    public const string NotesFile = "notes.json";

    /// <summary>
    /// The quality check's folder of the version (F11 + F14, 0.1.50): its report (<see cref="QualityReportFile"/>), the last review sheet and
    /// the character sheet. Written only when the template's quality mode reviewed the video.
    /// </summary>
    public const string Quality = "quality";

    /// <summary>Inside <see cref="Quality"/>: the report (<see cref="QualityReport"/>, JSON).</summary>
    public const string QualityReportFile = "report.json";

    /// <summary>The thumbnail (the Produced videos page makes it again when it is missing).</summary>
    public const string ThumbnailFile = "thumbnail.jpg";

    /// <summary>The render input inside <see cref="Source"/> (timing and texts of every scene; the engines read it under this name).</summary>
    public const string InputFile = Render.RenderInputBuilder.FileName;
}
