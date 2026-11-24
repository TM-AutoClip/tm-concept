namespace TMConcept.Core.Videos;

/// <summary>The codes of the missing pieces before production (F16, 0.1.49; the screen names them "video.gap.&lt;code&gt;").</summary>
public static class ProductionGaps
{
    /// <summary>No template is chosen (required; the rule of 17 Nov 2026, moved into the list).</summary>
    public const string Template = "template";

    /// <summary>No video code model is chosen (required).</summary>
    public const string CodeModel = "code-model";

    /// <summary>A chosen online model's provider has no API key (required; its detail names the provider).</summary>
    public const string Key = "key";

    /// <summary>The narration is off: the video will be silent (optional).</summary>
    public const string Voice = "voice";

    /// <summary>The template's own music file is not there (optional; skipped = the video is made without music).</summary>
    public const string MusicMissing = "music-missing";

    /// <summary>The template's own music file cannot be read as sound (optional; skipped = the video is made without music).</summary>
    public const string MusicUnreadable = "music-unreadable";

    /// <summary>The pronunciation dictionary of the video's language is empty (a suggestion; optional).</summary>
    public const string Pronunciation = "pronunciation";
}
